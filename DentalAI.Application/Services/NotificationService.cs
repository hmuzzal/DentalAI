using DentalAI.Application.Common.Interfaces;
using DentalAI.Application.Common.Interfaces.Persistence;
using DentalAI.Application.Common.Models;
using DentalAI.Application.Common.Notifications;
using DentalAI.Application.Common.Phone;
using DentalAI.Application.DTOs.Notifications;
using DentalAI.Domain.Entities;
using DentalAI.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace DentalAI.Application.Services;

/// <summary>
/// Queues messages (they're sent by <see cref="MessageDispatcher"/>), and the clinic's notification settings and log.
/// Queuing never throws into the caller: a notification problem must not undo a booking.
/// </summary>
public sealed class NotificationService(
    INotificationRepository repository,
    ICurrentUserService currentUser,
    IDateTimeProvider clock,
    IEmailSender email,
    ISmsSender sms,
    IAppLinks links,
    ILogger<NotificationService> logger)
{
    // ---- Queuing ----

    /// <summary>Queues a confirmation, reschedule or cancellation notice for an appointment, if the clinic sends them.</summary>
    public async Task QueueAppointmentAsync(Guid appointmentId, MessageKind kind, CancellationToken cancellationToken)
    {
        try
        {
            var data = await repository.GetAppointmentForMessageAsync(appointmentId, cancellationToken);
            if (data is null) return;
            var settings = await GetOrCreateSettingsAsync(data.Clinic.Id, cancellationToken);

            // A moved or cancelled appointment's pending reminders no longer apply (new ones follow the new time).
            if (kind is MessageKind.Rescheduled or MessageKind.Cancelled)
                await repository.CancelPendingForAppointmentAsync(appointmentId,
                    [MessageKind.Reminder, MessageKind.BookingConfirmation, MessageKind.Rescheduled], cancellationToken);

            if (settings.ConfirmationsEnabled)
                AddAppointmentMessage(data, settings, kind, reminderHours: null, clock.UtcNow);

            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not queue {Kind} for appointment {AppointmentId}", kind, appointmentId);
        }
    }

    /// <summary>Emails a staff invite link. The copy kept in the log has the link removed once it's sent.</summary>
    public async Task QueueStaffInviteAsync(User user, string clinicName, string token, DateTime expiresAtUtc, string? clinicTimeZone,
        CancellationToken cancellationToken)
    {
        try
        {
            var zone = SchedulingEngine.ResolveTimeZone(clinicTimeZone);
            var link = links.AcceptInvite(token);
            var role = user.Role == UserRole.ClinicAdmin ? "an admin" : "a receptionist";
            var (subject, body) = MessageTemplates.StaffInvite(user.FirstName, clinicName, role, link,
                TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(expiresAtUtc, DateTimeKind.Utc), zone));
            repository.AddMessage(new OutboundMessage
            {
                ClinicId = user.ClinicId,
                Channel = MessageChannel.Email,
                Kind = MessageKind.StaffInvite,
                To = user.Email,
                Subject = subject,
                Body = body,
                RedactBodyAfterSend = true,
                UserId = user.Id,
                NotBeforeUtc = clock.UtcNow
            });
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not queue the invite email for user {UserId}", user.Id);
        }
    }

    /// <summary>
    /// Sends a portal sign-in code right away (no quiet hours: the person just asked for it). The clinic's email and
    /// text switches don't apply, and the logged copy loses its text once sent.
    /// </summary>
    public async Task QueuePortalCodeAsync(Guid clinicId, string clinicName, MessageChannel channel, string to, string code,
        int validMinutes, Guid? patientId, CancellationToken cancellationToken)
    {
        try
        {
            var (subject, body) = MessageTemplates.PortalCode(channel, clinicName, code, validMinutes);
            repository.AddMessage(new OutboundMessage
            {
                ClinicId = clinicId,
                Channel = channel,
                Kind = MessageKind.PortalCode,
                To = to,
                Subject = subject,
                Body = body,
                RedactBodyAfterSend = true,
                PatientId = patientId,
                NotBeforeUtc = clock.UtcNow
            });
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not queue a portal sign-in code for clinic {ClinicId}", clinicId);
        }
    }

    /// <summary>Adds (does not save) one appointment message on the patient's channel, respecting quiet hours.</summary>
    internal OutboundMessage? AddAppointmentMessage(AppointmentForMessage data, ClinicNotificationSettings settings, MessageKind kind, int? reminderHours,
        DateTime nowUtc)
    {
        var channel = NotificationRules.ChooseChannel(data.Patient, settings);
        if (channel is null) return null;

        var zone = SchedulingEngine.ResolveTimeZone(data.Clinic.TimeZone);
        var now = nowUtc;
        var facts = new AppointmentFacts(
            data.Clinic.Name,
            string.IsNullOrWhiteSpace(data.Clinic.Phone) ? null : PhoneNumberRules.Display(data.Clinic.Phone),
            string.IsNullOrWhiteSpace(data.Clinic.Address) ? null : data.Clinic.Address,
            data.Patient.FirstName,
            $"Dr. {data.Doctor.LastName}".Trim(),
            data.Service.Name,
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(data.Appointment.StartTime, DateTimeKind.Utc), zone),
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(now, DateTimeKind.Utc), zone));
        var (subject, body) = MessageTemplates.Appointment(kind, channel.Value, facts);

        // Texts wait for the end of quiet hours (TCPA). If that's after the appointment, a reminder is pointless.
        var notBefore = channel == MessageChannel.Sms
            ? NotificationRules.NextAllowedUtc(now, zone, settings.QuietHoursStart, settings.QuietHoursEnd)
            : now;
        var start = DateTime.SpecifyKind(data.Appointment.StartTime, DateTimeKind.Utc);
        var pointless = kind == MessageKind.Reminder && notBefore >= start;

        var message = new OutboundMessage
        {
            ClinicId = data.Clinic.Id,
            Channel = channel.Value,
            Kind = kind,
            To = channel == MessageChannel.Sms ? data.Patient.Phone : data.Patient.Email!,
            Subject = subject,
            Body = body,
            PatientId = data.Patient.Id,
            AppointmentId = data.Appointment.Id,
            ReminderHours = reminderHours,
            NotBeforeUtc = notBefore,
            Status = pointless ? MessageStatus.Skipped : MessageStatus.Pending,
            LastError = pointless ? "Quiet hours last until after the appointment." : null
        };
        repository.AddMessage(message);
        return message;
    }

    internal async Task<ClinicNotificationSettings> GetOrCreateSettingsAsync(Guid clinicId, CancellationToken cancellationToken)
    {
        var settings = await repository.GetSettingsForClinicAsync(clinicId, cancellationToken);
        if (settings is not null) return settings;
        settings = new ClinicNotificationSettings { ClinicId = clinicId };
        repository.AddSettings(settings);
        return settings;
    }

    // ---- Settings, log and test (signed-in clinic) ----

    public async Task<Result<NotificationSettingsDto>> GetSettingsAsync(CancellationToken cancellationToken)
    {
        if (currentUser.ClinicId is not { } clinicId) return Forbidden<NotificationSettingsDto>();
        var settings = await GetOrCreateSettingsAsync(clinicId, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return Result<NotificationSettingsDto>.Ok(ToDto(settings));
    }

    public async Task<Result<NotificationSettingsDto>> UpdateSettingsAsync(UpdateNotificationSettingsRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.ClinicId is not { } clinicId) return Forbidden<NotificationSettingsDto>();
        var settings = await GetOrCreateSettingsAsync(clinicId, cancellationToken);
        settings.EmailEnabled = request.EmailEnabled;
        settings.SmsEnabled = request.SmsEnabled;
        settings.ConfirmationsEnabled = request.ConfirmationsEnabled;
        settings.RemindersEnabled = request.RemindersEnabled;
        settings.FirstReminderHours = request.FirstReminderHours;
        settings.SecondReminderHours = request.SecondReminderHours;
        settings.QuietHoursStart = request.QuietHoursStart;
        settings.QuietHoursEnd = request.QuietHoursEnd;
        await repository.SaveChangesAsync(cancellationToken);
        return Result<NotificationSettingsDto>.Ok(ToDto(settings), "Notification settings saved.");
    }

    public async Task<Result<PagedResult<OutboundMessageDto>>> ListAsync(MessageListQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.ClinicId is null) return Forbidden<PagedResult<OutboundMessageDto>>();
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, MessageListQuery.MaxPageSize);
        var (items, total) = await repository.ListAsync(query.Status, query.Channel, query.PatientId, query.AppointmentId,
            page, pageSize, cancellationToken);
        var names = await repository.GetPatientNamesAsync(
            items.Where(x => x.PatientId.HasValue).Select(x => x.PatientId!.Value).Distinct().ToList(), cancellationToken);
        var dtos = items.Select(m => ToDto(m, m.PatientId is { } id && names.TryGetValue(id, out var name) ? name : null)).ToList();
        return Result<PagedResult<OutboundMessageDto>>.Ok(new PagedResult<OutboundMessageDto>(dtos, total, page, pageSize));
    }

    public async Task<Result<OutboundMessageDto>> SendTestAsync(SendTestMessageRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.ClinicId is not { } clinicId) return Forbidden<OutboundMessageDto>();
        var to = request.To.Trim();
        if (request.Channel == MessageChannel.Sms)
        {
            if (!PhoneNumberRules.TryNormalize(to, null, out var e164))
                return Result<OutboundMessageDto>.Fail(ResultStatus.ValidationError, "Enter the phone number with its country code, e.g. +1 212 555 0142.");
            to = e164;
        }
        else if (!to.Contains('@'))
        {
            return Result<OutboundMessageDto>.Fail(ResultStatus.ValidationError, "Enter an email address.");
        }

        var message = new OutboundMessage
        {
            ClinicId = clinicId,
            Channel = request.Channel,
            Kind = MessageKind.Test,
            To = to,
            Subject = request.Channel == MessageChannel.Email ? "DentalAI test message" : null,
            Body = request.Channel == MessageChannel.Sms
                ? $"DentalAI test message. If you got this, texts work. {MessageTemplates.OptOut}"
                : "This is a test message from DentalAI. If you got it, email works.",
            UserId = currentUser.UserId,
            NotBeforeUtc = clock.UtcNow
        };
        repository.AddMessage(message);
        await repository.SaveChangesAsync(cancellationToken);
        return Result<OutboundMessageDto>.Ok(ToDto(message),
            (request.Channel == MessageChannel.Sms ? sms.IsLive : email.IsLive)
                ? "Test message queued. It should arrive within a minute."
                : "Test message queued, but no provider is set up, so it will only be written to the server log.");
    }

    private NotificationSettingsDto ToDto(ClinicNotificationSettings s) => new(
        s.EmailEnabled, s.SmsEnabled, s.ConfirmationsEnabled, s.RemindersEnabled, s.FirstReminderHours, s.SecondReminderHours,
        s.QuietHoursStart, s.QuietHoursEnd, email.IsLive, sms.IsLive);

    private static OutboundMessageDto ToDto(OutboundMessage m, string? patientName = null) => new(
        m.Id, m.Channel, m.Kind, m.To, m.Subject,
        // Staff never see a sign-in code, not even while it waits to be sent: it would let them sign in as the patient.
        m.Kind == MessageKind.PortalCode ? "[Sign-in code hidden]" : m.Body, m.Status, m.PatientId, patientName, m.AppointmentId, m.ReminderHours,
        DateTime.SpecifyKind(m.CreatedDate, DateTimeKind.Utc), DateTime.SpecifyKind(m.NotBeforeUtc, DateTimeKind.Utc),
        m.SentAtUtc is { } sent ? DateTime.SpecifyKind(sent, DateTimeKind.Utc) : null, m.Attempts, m.LastError);

    private static Result<T> Forbidden<T>() => Result<T>.Fail(ResultStatus.Forbidden, "Clinic context is required.");
}
