using DentalAI.Application.Common.Models;
using DentalAI.Application.Services;
using DentalAI.Domain.Enums;
using Xunit;

namespace DentalAI.Application.Tests.Services;

public sealed class DashboardCalculatorTests
{
    private const string Dhaka = "Asia/Dhaka";          // UTC+6, no DST
    private const string NewYork = "America/New_York";  // UTC-5 in November

    // 2026-11-10 22:00 UTC = 2026-11-11 04:00 in Dhaka = 2026-11-10 17:00 in New York.
    private static readonly DateTime Now = Utc(2026, 11, 10, 22, 0);

    [Fact]
    public void Today_FollowsTheClinicCalendar_NotUtc()
    {
        var rows = new[]
        {
            // Nov 11 01:00 Dhaka: "today" in Dhaka, although it is Nov 10 in UTC.
            Row(Utc(2026, 11, 10, 19, 0), Dhaka),
            // Nov 10 20:00 Dhaka: same UTC day as now, but "yesterday" for the clinic.
            Row(Utc(2026, 11, 10, 14, 0), Dhaka),
            // Nov 10 18:00 New York: "today" there, although it is Nov 11 in UTC.
            Row(Utc(2026, 11, 10, 23, 0), NewYork)
        };

        var result = DashboardCalculator.Build(rows, [], 0, Now);

        Assert.Equal(2, result.TodayAppointments);
    }

    [Fact]
    public void TodayCounts_SplitByStatus()
    {
        var rows = new[]
        {
            Row(Utc(2026, 11, 10, 20, 0), Dhaka, AppointmentStatus.Completed),
            Row(Utc(2026, 11, 10, 20, 30), Dhaka, AppointmentStatus.Cancelled),
            Row(Utc(2026, 11, 10, 21, 0), Dhaka, AppointmentStatus.NoShow),
            Row(Utc(2026, 11, 10, 21, 30), Dhaka, AppointmentStatus.Pending)
        };

        var result = DashboardCalculator.Build(rows, [], 7, Now);

        Assert.Equal(4, result.TodayAppointments);
        Assert.Equal(1, result.CompletedToday);
        Assert.Equal(1, result.CancelledToday);
        Assert.Equal(1, result.NoShowsToday);
        Assert.Equal(7, result.UpcomingAppointments);
    }

    [Fact]
    public void RevenueByDay_UsesTheClinicLocalDate()
    {
        var completed = new[]
        {
            // 2026-11-05 20:00 UTC is Nov 6 in Dhaka.
            Row(Utc(2026, 11, 5, 20, 0), Dhaka, AppointmentStatus.Completed, 80m),
            Row(Utc(2026, 11, 6, 3, 0), Dhaka, AppointmentStatus.Completed, 20m)
        };

        var result = DashboardCalculator.Build([], completed, 0, Now);

        var point = Assert.Single(result.RevenueByDay);
        Assert.Equal("2026-11-06", point.Period);
        Assert.Equal(100m, point.Revenue);
    }

    [Fact]
    public void RevenueByDay_KeepsOnlyTheLast30ClinicDays()
    {
        // Clinic today (Dhaka) is Nov 11, so the window is Oct 13 – Nov 11.
        var completed = new[]
        {
            Row(Utc(2026, 10, 12, 6, 0), Dhaka, AppointmentStatus.Completed, 10m), // Oct 12: out
            Row(Utc(2026, 10, 13, 6, 0), Dhaka, AppointmentStatus.Completed, 20m), // Oct 13: in
            Row(Utc(2026, 11, 11, 2, 0), Dhaka, AppointmentStatus.Completed, 30m)  // Nov 11 08:00 local: in (later than now, still today)
        };

        var result = DashboardCalculator.Build([], completed, 0, Now);

        Assert.Equal(["2026-10-13", "2026-11-11"], result.RevenueByDay.Select(p => p.Period));
    }

    [Fact]
    public void RevenueByMonth_UsesTheClinicLocalMonth_AndLast12Months()
    {
        var completed = new[]
        {
            // Oct 31 20:00 UTC is Nov 1 in Dhaka → November.
            Row(Utc(2026, 10, 31, 20, 0), Dhaka, AppointmentStatus.Completed, 50m),
            Row(Utc(2026, 10, 15, 6, 0), Dhaka, AppointmentStatus.Completed, 40m),
            // Nov 2025 is 12 months before Nov 2026: outside the 12-month window.
            Row(Utc(2025, 11, 15, 6, 0), Dhaka, AppointmentStatus.Completed, 999m)
        };

        var result = DashboardCalculator.Build([], completed, 0, Now);

        Assert.Equal(["2026-10", "2026-11"], result.RevenueByMonth.Select(p => p.Period));
        Assert.Equal([40m, 50m], result.RevenueByMonth.Select(p => p.Revenue));
    }

    [Fact]
    public void Revenue_IgnoresAppointmentsThatAreNotCompleted()
    {
        var rows = new[] { Row(Utc(2026, 11, 9, 6, 0), Dhaka, AppointmentStatus.Cancelled, 80m) };

        var result = DashboardCalculator.Build([], rows, 0, Now);

        Assert.Empty(result.RevenueByDay);
        Assert.Empty(result.RevenueByMonth);
    }

    [Fact]
    public void Windows_CoverTodayInEveryZone()
    {
        var (fromUtc, toUtc) = DashboardCalculator.TodayWindowUtc(Now);

        // Start of "today" in the furthest-ahead zone and end of "today" in the furthest-behind zone.
        Assert.True(fromUtc <= Utc(2026, 11, 10, 10, 0));   // Nov 11 00:00 at UTC+14
        Assert.True(toUtc >= Utc(2026, 11, 11, 12, 0));     // Nov 11 00:00 at UTC-12, end of its Nov 10
    }

    private static DashboardAppointmentRow Row(
        DateTime startUtc, string zone, AppointmentStatus status = AppointmentStatus.Pending, decimal price = 0m) =>
        new(startUtc, status, price, zone);

    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);
}
