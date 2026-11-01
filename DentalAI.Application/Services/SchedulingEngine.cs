using DentalAI.Application.Common.Models;
using DentalAI.Application.DTOs.Scheduling;

namespace DentalAI.Application.Services;

/// <summary>
/// Turns a doctor's weekly hours into bookable slots and validates bookings.
/// Working hours and breaks are clinic wall-clock times; appointments are UTC instants.
/// Daylight saving: a wall-clock time skipped by spring-forward is never offered as a slot, and a wall-clock
/// time repeated by fall-back means its first occurrence. An appointment lasts its real (UTC) duration; the
/// working-hour and break checks use the wall-clock span <c>start + duration</c>.
/// </summary>
public sealed class SchedulingEngine
{
    public const int DefaultSlotIntervalMinutes = 15;

    public IReadOnlyList<AvailableSlotDto> GenerateSlots(SchedulingSnapshot snapshot, int durationMinutes, DateTime utcNow)
    {
        if (durationMinutes <= 0 || snapshot.IsOnLeave)
        {
            return [];
        }

        var timeZone = ResolveTimeZone(snapshot.TimeZoneId);
        var duration = TimeSpan.FromMinutes(durationMinutes);
        var interval = snapshot.SlotIntervalMinutes > 0 ? snapshot.SlotIntervalMinutes : DefaultSlotIntervalMinutes;
        var slots = new List<AvailableSlotDto>();

        foreach (var period in snapshot.WorkingPeriods.OrderBy(x => x.Start))
        {
            var cursor = period.Start;
            while (cursor < period.End)
            {
                var end = cursor.Add(duration, out var wrappedDays);
                if (wrappedDays != 0 || end > period.End)
                {
                    break;
                }

                if (TryToUtcExact(snapshot.Date, cursor, timeZone, out var startUtc))
                {
                    var endUtc = startUtc.Add(duration);
                    if (startUtc > utcNow
                        && !OverlapsLocal(cursor, end, snapshot.BreakPeriods)
                        && !snapshot.Appointments.Any(x => startUtc < AsUtc(x.EndUtc) && endUtc > AsUtc(x.StartUtc)))
                    {
                        slots.Add(new AvailableSlotDto(startUtc, endUtc, snapshot.TimeZoneId));
                    }
                }

                var next = cursor.AddMinutes(interval, out wrappedDays);
                if (wrappedDays != 0) break;
                cursor = next;
            }
        }

        return slots;
    }

    public IReadOnlyList<string> Validate(SchedulingSnapshot snapshot, DateTime startUtc, DateTime endUtc)
    {
        startUtc = AsUtc(startUtc);
        endUtc = AsUtc(endUtc);
        if (endUtc <= startUtc)
        {
            return ["End time must be later than start time."];
        }

        var errors = new List<string>();
        if (snapshot.IsOnLeave)
        {
            errors.Add("The doctor is on leave for the selected date.");
        }

        var timeZone = ResolveTimeZone(snapshot.TimeZoneId);
        var localStart = TimeZoneInfo.ConvertTimeFromUtc(startUtc, timeZone);
        var start = TimeOnly.FromDateTime(localStart);
        var end = start.Add(endUtc - startUtc, out var wrappedDays);

        if (wrappedDays != 0
            || DateOnly.FromDateTime(localStart) != snapshot.Date
            || !snapshot.WorkingPeriods.Any(x => start >= x.Start && end <= x.End))
        {
            errors.Add("The appointment is outside the doctor's working hours.");
        }

        if (wrappedDays == 0 && OverlapsLocal(start, end, snapshot.BreakPeriods))
        {
            errors.Add("The appointment overlaps a doctor break.");
        }

        if (snapshot.Appointments.Any(x => startUtc < AsUtc(x.EndUtc) && endUtc > AsUtc(x.StartUtc)))
        {
            errors.Add("The selected slot is already booked.");
        }

        return errors;
    }

    public static TimeZoneInfo ResolveTimeZone(string? id) =>
        TryFindTimeZone(id, out var timeZone) ? timeZone : TimeZoneInfo.Utc;

    /// <summary>True when <paramref name="id"/> is a time-zone ID this server recognises (IANA or Windows).</summary>
    public static bool TryFindTimeZone(string? id, out TimeZoneInfo timeZone)
    {
        timeZone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(id)) return false;
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(id.Trim(), out var found)) return false;
        timeZone = found;
        return true;
    }

    /// <summary>
    /// Converts a clinic wall-clock time to UTC without ever throwing. A time repeated by fall-back resolves to
    /// its first occurrence; a time skipped by spring-forward is read with the offset in force before the jump
    /// (so 02:30 on a New York spring-forward day becomes 03:30 EDT). Use <see cref="TryToUtcExact"/> to detect it.
    /// </summary>
    public static DateTime ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo timeZone)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);

        if (timeZone.IsInvalidTime(local))
        {
            var offsetBefore = Min(timeZone.GetUtcOffset(local.AddHours(-12)), timeZone.GetUtcOffset(local.AddHours(12)));
            return DateTime.SpecifyKind(local - offsetBefore, DateTimeKind.Utc);
        }

        if (timeZone.IsAmbiguousTime(local))
        {
            // The first occurrence is the one with the larger offset (still on daylight time).
            var offset = timeZone.GetAmbiguousTimeOffsets(local).Max();
            return DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, timeZone);
    }

    /// <summary>Like <see cref="ToUtc"/>, but returns false for a wall-clock time that does not exist.</summary>
    public static bool TryToUtcExact(DateOnly date, TimeOnly time, TimeZoneInfo timeZone, out DateTime utc)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(local))
        {
            utc = default;
            return false;
        }

        utc = ToUtc(date, time, timeZone);
        return true;
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private static bool OverlapsLocal(TimeOnly start, TimeOnly end, IEnumerable<ScheduleWindow> windows) =>
        windows.Any(x => start < x.End && end > x.Start);

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
