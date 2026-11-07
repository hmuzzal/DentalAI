using DentalAI.Domain.Entities;
using DentalAI.Domain.Enums;

namespace DentalAI.Application.Common.Notifications;

/// <summary>Who gets what, when, and how: channel choice, quiet hours and reminder timing. Pure functions.</summary>
public static class NotificationRules
{
    /// <summary>
    /// The channel for a patient message, or null if there's no way to reach them. Texts only with SMS consent (TCPA).
    /// The patient's preference wins when it's possible; otherwise the other channel.
    /// </summary>
    public static MessageChannel? ChooseChannel(Patient patient, ClinicNotificationSettings settings)
    {
        var canText = settings.SmsEnabled && patient.SmsOptIn && patient.Phone.StartsWith('+');
        var canEmail = settings.EmailEnabled && !string.IsNullOrWhiteSpace(patient.Email);

        return patient.PreferredContactMethod switch
        {
            ContactMethod.Email when canEmail => MessageChannel.Email,
            ContactMethod.Sms when canText => MessageChannel.Sms,
            _ when canText => MessageChannel.Sms,
            _ when canEmail => MessageChannel.Email,
            _ => null
        };
    }

    /// <summary>
    /// The first moment at or after <paramref name="utc"/> outside quiet hours in the clinic's zone.
    /// Quiet hours may wrap midnight (21:00–08:00) or not (13:00–14:00). Equal start and end means none.
    /// </summary>
    public static DateTime NextAllowedUtc(DateTime utc, TimeZoneInfo zone, TimeOnly quietStart, TimeOnly quietEnd)
    {
        if (quietStart == quietEnd) return utc;
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
        var time = TimeOnly.FromDateTime(local);
        var wraps = quietStart > quietEnd;
        var quiet = wraps ? time >= quietStart || time < quietEnd : time >= quietStart && time < quietEnd;
        if (!quiet) return utc;

        var endDate = DateOnly.FromDateTime(local);
        if (wraps && time >= quietStart) endDate = endDate.AddDays(1); // evening: quiet ends tomorrow morning
        return Services.SchedulingEngine.ToUtc(endDate, quietEnd, zone);
    }

    /// <summary>
    /// The reminder slots (hours before) that are due now. A slot is due from (start − hours) until the appointment,
    /// and only if the appointment was booked before that point; a late booking already got its confirmation.
    /// </summary>
    public static IEnumerable<int> DueReminderHours(DateTime startUtc, DateTime bookedAtUtc, DateTime nowUtc, IEnumerable<int> slots)
    {
        foreach (var hours in slots.Where(h => h > 0).Distinct().OrderByDescending(h => h))
        {
            var sendAt = startUtc.AddHours(-hours);
            if (nowUtc >= sendAt && nowUtc < startUtc && bookedAtUtc <= sendAt)
            {
                // Only the latest due slot: if the 24 h one is late enough that the 2 h one is due too, send just one.
                var smaller = slots.Where(h => h > 0 && h < hours).ToList();
                if (smaller.Any(h => nowUtc >= startUtc.AddHours(-h))) continue;
                yield return hours;
            }
        }
    }

    /// <summary>Retry delays after a failed send: 1, 5, 15, 60 minutes; then give up.</summary>
    public static TimeSpan? RetryDelay(int attempts) => attempts switch
    {
        1 => TimeSpan.FromMinutes(1),
        2 => TimeSpan.FromMinutes(5),
        3 => TimeSpan.FromMinutes(15),
        4 => TimeSpan.FromMinutes(60),
        _ => null
    };
}
