using DentalAI.Application.Common.Models;
using DentalAI.Application.DTOs.Scheduling;

namespace DentalAI.Application.Services;

public sealed class SchedulingEngine
{
    public IReadOnlyList<AvailableSlotDto> GenerateSlots(SchedulingSnapshot snapshot, int durationMinutes, DateTime utcNow)
    {
        if (durationMinutes <= 0 || snapshot.IsOnLeave)
        {
            return [];
        }

        var timeZone = ResolveTimeZone(snapshot.TimeZoneId);
        var duration = TimeSpan.FromMinutes(durationMinutes);
        var slots = new List<AvailableSlotDto>();

        foreach (var period in snapshot.WorkingPeriods.OrderBy(x => x.Start))
        {
            var cursor = period.Start;
            while (cursor.Add(duration) <= period.End)
            {
                var startUtc = ToUtc(snapshot.Date, cursor, timeZone);
                var endUtc = ToUtc(snapshot.Date, cursor.Add(duration), timeZone);

                if (startUtc > utcNow
                    && !OverlapsLocal(cursor, cursor.Add(duration), snapshot.BreakPeriods)
                    && !snapshot.Appointments.Any(x => startUtc < x.EndUtc && endUtc > x.StartUtc))
                {
                    slots.Add(new AvailableSlotDto(startUtc, endUtc));
                }

                cursor = cursor.AddMinutes(15);
            }
        }

        return slots;
    }

    public IReadOnlyList<string> Validate(SchedulingSnapshot snapshot, DateTime startUtc, DateTime endUtc)
    {
        var errors = new List<string>();
        if (endUtc <= startUtc)
        {
            return ["End time must be later than start time."];
        }

        if (snapshot.IsOnLeave)
        {
            errors.Add("The doctor is on leave for the selected date.");
        }

        var timeZone = ResolveTimeZone(snapshot.TimeZoneId);
        var localStart = TimeZoneInfo.ConvertTimeFromUtc(AsUtc(startUtc), timeZone);
        var localEnd = TimeZoneInfo.ConvertTimeFromUtc(AsUtc(endUtc), timeZone);
        var start = TimeOnly.FromDateTime(localStart);
        var end = TimeOnly.FromDateTime(localEnd);

        if (localStart.Date != localEnd.Date
            || !snapshot.WorkingPeriods.Any(x => start >= x.Start && end <= x.End))
        {
            errors.Add("The appointment is outside the doctor's working hours.");
        }

        if (OverlapsLocal(start, end, snapshot.BreakPeriods))
        {
            errors.Add("The appointment overlaps a doctor break.");
        }

        if (snapshot.Appointments.Any(x => AsUtc(startUtc) < x.EndUtc && AsUtc(endUtc) > x.StartUtc))
        {
            errors.Add("The selected slot is already booked.");
        }

        return errors;
    }

    public static TimeZoneInfo ResolveTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return TimeZoneInfo.Utc;
        }

        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
        catch (InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
    }

    public static DateTime ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo timeZone)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, timeZone);
    }

    private static bool OverlapsLocal(TimeOnly start, TimeOnly end, IEnumerable<ScheduleWindow> windows) =>
        windows.Any(x => start < x.End && end > x.Start);

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
