using DentalAI.Application.Common.Models;
using DentalAI.Application.DTOs.Dashboard;
using DentalAI.Domain.Enums;

namespace DentalAI.Application.Services;

/// <summary>
/// Dashboard numbers by clinic-local calendar. Each appointment counts toward "today" and toward a revenue
/// day/month in its own clinic's time zone, so a platform admin seeing several clinics gets each one right.
/// Pure functions: the repository supplies rows, this decides which calendar day they belong to.
/// </summary>
public static class DashboardCalculator
{
    public const int RevenueDays = 30;
    public const int RevenueMonths = 12;

    /// <summary>UTC range that contains "today" in every time zone (offsets run from -12h to +14h).</summary>
    public static (DateTime FromUtc, DateTime ToUtc) TodayWindowUtc(DateTime utcNow) =>
        (AsUtc(utcNow).AddDays(-2), AsUtc(utcNow).AddDays(2));

    /// <summary>UTC range that contains the last <see cref="RevenueMonths"/> clinic-local months in every zone.</summary>
    public static (DateTime FromUtc, DateTime ToUtc) RevenueWindowUtc(DateTime utcNow)
    {
        var now = AsUtc(utcNow);
        var firstOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return (firstOfMonth.AddMonths(-(RevenueMonths - 1)).AddDays(-1), now.AddDays(1));
    }

    public static SchedulingDashboardDto Build(
        IReadOnlyList<DashboardAppointmentRow> todayWindowRows,
        IReadOnlyList<DashboardAppointmentRow> completedRows,
        int upcomingAppointments,
        DateTime utcNow)
    {
        var today = todayWindowRows.Where(row => IsToday(row, utcNow)).ToList();

        var byDay = new SortedDictionary<string, decimal>(StringComparer.Ordinal);
        var byMonth = new SortedDictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var row in completedRows.Where(row => row.Status == AppointmentStatus.Completed))
        {
            var (localDate, localToday) = LocalDates(row, utcNow);
            if (localDate > localToday) continue;

            if (localDate > localToday.AddDays(-RevenueDays))
            {
                Add(byDay, localDate.ToString("yyyy-MM-dd"), row.Price);
            }

            var monthsAgo = (localToday.Year * 12 + localToday.Month) - (localDate.Year * 12 + localDate.Month);
            if (monthsAgo < RevenueMonths)
            {
                Add(byMonth, $"{localDate.Year:D4}-{localDate.Month:D2}", row.Price);
            }
        }

        return new SchedulingDashboardDto(
            today.Count,
            upcomingAppointments,
            today.Count(row => row.Status == AppointmentStatus.Completed),
            today.Count(row => row.Status == AppointmentStatus.Cancelled),
            today.Count(row => row.Status == AppointmentStatus.NoShow),
            byDay.Select(pair => new RevenuePointDto(pair.Key, pair.Value)).ToList(),
            byMonth.Select(pair => new RevenuePointDto(pair.Key, pair.Value)).ToList());
    }

    /// <summary>True when the appointment falls on today's date in its own clinic's zone.</summary>
    public static bool IsToday(DashboardAppointmentRow row, DateTime utcNow)
    {
        var (localDate, localToday) = LocalDates(row, utcNow);
        return localDate == localToday;
    }

    private static (DateOnly LocalDate, DateOnly LocalToday) LocalDates(DashboardAppointmentRow row, DateTime utcNow)
    {
        var zone = SchedulingEngine.ResolveTimeZone(row.TimeZoneId);
        return (
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(AsUtc(row.StartUtc), zone)),
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utcNow), zone)));
    }

    private static void Add(IDictionary<string, decimal> totals, string key, decimal amount) =>
        totals[key] = totals.TryGetValue(key, out var current) ? current + amount : amount;

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
