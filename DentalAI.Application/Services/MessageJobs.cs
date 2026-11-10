using DentalAI.Application.Common.Interfaces;
using DentalAI.Application.Common.Interfaces.Persistence;
using DentalAI.Application.Common.Notifications;
using DentalAI.Domain.Entities;
using DentalAI.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace DentalAI.Application.Services;

/// <summary>Queues reminders that are due. Runs every minute in the background; safe to run twice (no duplicates).</summary>
public sealed class ReminderScheduler(
    INotificationRepository repository,
    NotificationService notifications,
    ILogger<ReminderScheduler> logger)
{
    /// <summary>The longest reminder lead time we look ahead for.</summary>
    private const int MaxLeadHours = 72;

    public async Task<int> RunOnceAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var candidates = await repository.GetReminderCandidatesAsync(nowUtc, nowUtc.AddHours(MaxLeadHours), cancellationToken);
        var queued = 0;
        var settingsByClinic = new Dictionary<Guid, ClinicNotificationSettings>();

        foreach (var data in candidates)
        {
            if (!settingsByClinic.TryGetValue(data.Clinic.Id, out var settings))
                settingsByClinic[data.Clinic.Id] = settings = await notifications.GetOrCreateSettingsAsync(data.Clinic.Id, cancellationToken);
            if (!settings.RemindersEnabled) continue;

            var start = DateTime.SpecifyKind(data.Appointment.StartTime, DateTimeKind.Utc);
            var booked = DateTime.SpecifyKind(data.Appointment.CreatedDate, DateTimeKind.Utc);
            foreach (var hours in NotificationRules.DueReminderHours(start, booked, nowUtc, [settings.FirstReminderHours, settings.SecondReminderHours]))
            {
                if (await repository.ReminderExistsAsync(data.Appointment.Id, hours, cancellationToken)) continue;
                if (notifications.AddAppointmentMessage(data, settings, MessageKind.Reminder, hours, nowUtc) is not null) queued++;
            }
        }

        await repository.SaveChangesAsync(cancellationToken);
        if (queued > 0) logger.LogInformation("Queued {Count} reminder(s)", queued);
        return queued;
    }
}

/// <summary>
/// Sends due messages from the outbox. One instance at a time (an app lock), so a message is never sent twice by two
/// servers. Failures are retried with growing delays, then marked Failed. Runs every few seconds in the background.
/// </summary>
public sealed class MessageDispatcher(
    INotificationRepository repository,
    IUnitOfWork unitOfWork,
    IEmailSender email,
    ISmsSender sms,
    ILogger<MessageDispatcher> logger)
{
    private const int BatchSize = 20;
    private static readonly TimeSpan ClaimTimeout = TimeSpan.FromMinutes(5);

    public Task<int> RunOnceAsync(DateTime nowUtc, CancellationToken cancellationToken) =>
        unitOfWork.RunWithLockAsync("notification-dispatch", async ct =>
        {
            await repository.ReleaseStaleClaimsAsync(nowUtc - ClaimTimeout, ct);
            var batch = await repository.ClaimDueAsync(nowUtc, BatchSize, ct);
            foreach (var message in batch)
            {
                await SendOneAsync(message, nowUtc, ct);
            }
            await repository.SaveChangesAsync(ct);
            return batch.Count;
        }, cancellationToken);

    private async Task SendOneAsync(OutboundMessage message, DateTime nowUtc, CancellationToken cancellationToken)
    {
        SendResult result;
        try
        {
            result = message.Channel == MessageChannel.Sms
                ? await sms.SendAsync(message.To, message.Body, cancellationToken)
                : await email.SendAsync(new EmailMessage(message.To, message.Subject ?? "(no subject)", message.Body), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            result = SendResult.Retry(exception.Message);
        }

        message.Attempts++;
        message.ClaimedAtUtc = null;
        if (result.Success)
        {
            message.Status = MessageStatus.Sent;
            message.SentAtUtc = nowUtc;
            message.ProviderMessageId = result.ProviderMessageId;
            message.LastError = null;
            if (message.RedactBodyAfterSend) message.Body = "[Contents removed after sending: it contained a one-time link or code.]";
            return;
        }

        var retryIn = result.Permanent ? null : NotificationRules.RetryDelay(message.Attempts);
        message.LastError = Truncate(result.Error, 500);
        if (retryIn is { } delay)
        {
            message.Status = MessageStatus.Pending;
            message.NotBeforeUtc = nowUtc + delay;
        }
        else
        {
            message.Status = MessageStatus.Failed;
            logger.LogWarning("Gave up on {Channel} message {MessageId} after {Attempts} attempt(s): {Error}",
                message.Channel, message.Id, message.Attempts, message.LastError);
        }
    }

    private static string? Truncate(string? value, int max) => value is { Length: > 0 } && value.Length > max ? value[..max] : value;
}

/// <summary>
/// Patients' replies to texts. STOP (and STOPALL, UNSUBSCRIBE, CANCEL, END, QUIT) withdraws SMS consent everywhere
/// this number is a patient; START / UNSTOP gives it back; C / CONFIRM / YES confirms the appointment from the latest
/// appointment text. Returns the text to reply with, or null for none (carriers answer STOP themselves).
/// </summary>
public sealed class InboundSmsHandler(
    INotificationRepository repository,
    IDateTimeProvider clock,
    ILogger<InboundSmsHandler> logger)
{
    private static readonly HashSet<string> Stop = ["STOP", "STOPALL", "UNSUBSCRIBE", "CANCEL", "END", "QUIT", "REVOKE", "OPTOUT"];
    private static readonly HashSet<string> Start = ["START", "UNSTOP", "YES START", "OPTIN"];
    private static readonly HashSet<string> Confirm = ["C", "CONFIRM", "CONFIRMED", "Y", "YES"];

    public async Task<string?> HandleAsync(string fromE164, string body, CancellationToken cancellationToken)
    {
        var word = (body ?? string.Empty).Trim().TrimEnd('.', '!').ToUpperInvariant();
        var now = clock.UtcNow;

        if (Stop.Contains(word) || Start.Contains(word))
        {
            var optIn = Start.Contains(word);
            foreach (var patient in await repository.FindPatientsByPhoneAsync(fromE164, cancellationToken))
            {
                if (patient.SmsOptIn == optIn) continue;
                patient.SmsOptIn = optIn;
                if (optIn) { patient.SmsOptInAtUtc = now; patient.SmsOptOutAtUtc = null; }
                else patient.SmsOptOutAtUtc = now;
            }
            await repository.SaveChangesAsync(cancellationToken);
            logger.LogInformation("SMS {Keyword} from a patient number", optIn ? "opt-in" : "opt-out");
            return null; // the carrier / Twilio sends the standard STOP and START confirmations
        }

        if (Confirm.Contains(word))
        {
            var last = await repository.LatestAppointmentTextToAsync(fromE164, now.AddDays(-3), cancellationToken);
            var appointment = last?.AppointmentId is { } id ? await repository.GetAppointmentTrackedAsync(id, cancellationToken) : null;
            if (appointment is null || appointment.StartTime <= now)
                return "We couldn't find an upcoming appointment to confirm. Please call the clinic.";
            if (appointment.Status == AppointmentStatus.Pending)
            {
                appointment.Status = AppointmentStatus.Confirmed;
                await repository.SaveChangesAsync(cancellationToken);
            }
            return appointment.Status == AppointmentStatus.Confirmed
                ? "Thanks, your appointment is confirmed. See you then!"
                : "This appointment can't be confirmed by text. Please call the clinic.";
        }

        return null; // anything else: no automatic reply; staff see it in the provider's console
    }
}
