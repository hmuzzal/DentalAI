using DentalAI.Application.Common.Models;
using DentalAI.Application.DTOs.Scheduling;
using DentalAI.Application.Services;
using Xunit;

namespace DentalAI.Application.Tests.Services;

/// <summary>
/// Daylight saving and multi-zone behaviour. US 2026: clocks spring forward on Sunday 8 March at 02:00
/// (02:00–02:59 does not exist) and fall back on Sunday 1 November at 02:00 (01:00–01:59 happens twice).
/// </summary>
public sealed class SchedulingEngineDstTests
{
    private const string NewYork = "America/New_York";
    private static readonly DateOnly SpringForward = new(2026, 3, 8);
    private static readonly DateOnly FallBack = new(2026, 11, 1);
    private static readonly DateTime LongAgo = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TimeZoneInfo NewYorkZone = SchedulingEngine.ResolveTimeZone(NewYork);

    private readonly SchedulingEngine _engine = new();

    [Fact]
    public void ToUtc_SkippedSpringForwardTime_UsesTheOffsetBeforeTheJumpAndDoesNotThrow()
    {
        var utc = SchedulingEngine.ToUtc(SpringForward, new TimeOnly(2, 30), NewYorkZone);

        Assert.Equal(Utc(2026, 3, 8, 7, 30), utc); // 02:30 EST would be 07:30Z, shown as 03:30 EDT
        Assert.False(SchedulingEngine.TryToUtcExact(SpringForward, new TimeOnly(2, 30), NewYorkZone, out _));
    }

    [Fact]
    public void ToUtc_RepeatedFallBackTime_PicksTheFirstOccurrence()
    {
        var utc = SchedulingEngine.ToUtc(FallBack, new TimeOnly(1, 30), NewYorkZone);

        Assert.Equal(Utc(2026, 11, 1, 5, 30), utc); // 01:30 EDT (UTC-4), not 01:30 EST (06:30Z)
        Assert.True(SchedulingEngine.TryToUtcExact(FallBack, new TimeOnly(1, 30), NewYorkZone, out var exact));
        Assert.Equal(utc, exact);
    }

    [Theory]
    [InlineData(2026, 3, 7, 14)]   // Saturday before spring-forward: EST, UTC-5
    [InlineData(2026, 3, 9, 13)]   // Monday after: EDT, UTC-4
    [InlineData(2026, 10, 31, 13)] // Saturday before fall-back: EDT
    [InlineData(2026, 11, 2, 14)]  // Monday after: EST
    public void ToUtc_NineAmAroundTheTransitions_UsesTheRightOffset(int year, int month, int day, int expectedUtcHour)
    {
        var utc = SchedulingEngine.ToUtc(new DateOnly(year, month, day), new TimeOnly(9, 0), NewYorkZone);
        Assert.Equal(Utc(year, month, day, expectedUtcHour, 0), utc);
    }

    [Fact]
    public void GenerateSlots_SpringForwardDay_SkipsTheMissingHour()
    {
        var snapshot = Snapshot(NewYork, SpringForward, interval: 30, working: [new(new(1, 0), new(4, 0))]);

        var slots = _engine.GenerateSlots(snapshot, 30, LongAgo);

        Assert.Equal(
            [Utc(2026, 3, 8, 6, 0), Utc(2026, 3, 8, 6, 30), Utc(2026, 3, 8, 7, 0), Utc(2026, 3, 8, 7, 30)],
            slots.Select(x => x.StartTimeUtc));
        Assert.All(slots, x => Assert.Equal(TimeSpan.FromMinutes(30), x.EndTimeUtc - x.StartTimeUtc));
        AssertNoOverlaps(slots);
    }

    [Fact]
    public void GenerateSlots_FallBackDay_OffersTheRepeatedHourOnceAndLastsTheRealDuration()
    {
        var snapshot = Snapshot(NewYork, FallBack, interval: 60, working: [new(new(0, 0), new(3, 0))]);

        var slots = _engine.GenerateSlots(snapshot, 60, LongAgo);

        Assert.Equal(
            [Utc(2026, 11, 1, 4, 0), Utc(2026, 11, 1, 5, 0), Utc(2026, 11, 1, 7, 0)],
            slots.Select(x => x.StartTimeUtc));
        Assert.All(slots, x => Assert.Equal(TimeSpan.FromHours(1), x.EndTimeUtc - x.StartTimeUtc));
        AssertNoOverlaps(slots);
    }

    [Fact]
    public void GenerateSlots_NormalClinicHoursOnTransitionDays_StayOnTheLocalGrid()
    {
        foreach (var (date, offsetHours) in new[] { (SpringForward, 4), (FallBack, 5) })
        {
            var snapshot = Snapshot(NewYork, date, working: [new(new(9, 0), new(10, 0))]);

            var slots = _engine.GenerateSlots(snapshot, 30, LongAgo);

            Assert.Equal(3, slots.Count);
            Assert.Equal(date.ToDateTime(new TimeOnly(9 + offsetHours, 0), DateTimeKind.Utc), slots[0].StartTimeUtc);
        }
    }

    [Theory]
    [InlineData("2026-03-08")]
    [InlineData("2026-11-01")]
    public void EveryGeneratedSlotOnATransitionDay_PassesValidation(string day)
    {
        var date = DateOnly.Parse(day);
        var snapshot = Snapshot(NewYork, date, working: [new(new(0, 0), new(23, 0))]);

        var slots = _engine.GenerateSlots(snapshot, 45, LongAgo);

        Assert.NotEmpty(slots);
        Assert.All(slots, slot => Assert.Empty(_engine.Validate(snapshot, slot.StartTimeUtc, slot.EndTimeUtc)));
    }

    [Theory]
    [InlineData(NewYork, 13)]
    [InlineData("America/Los_Angeles", 16)]
    [InlineData("America/Phoenix", 16)]
    [InlineData("Asia/Dhaka", 3)]
    [InlineData("UTC", 9)]
    public void GenerateSlots_NineAmClinicTime_DependsOnTheClinicZone(string zone, int expectedUtcHour)
    {
        var date = new DateOnly(2026, 7, 6);
        var snapshot = Snapshot(zone, date, working: [new(new(9, 0), new(9, 30))]);

        var slot = Assert.Single(_engine.GenerateSlots(snapshot, 30, LongAgo));

        Assert.Equal(Utc(2026, 7, 6, expectedUtcHour, 0), slot.StartTimeUtc);
        Assert.Equal(zone, slot.ClinicTimeZone);
    }

    [Fact]
    public void Validate_SameUtcInstant_IsInsideHoursInOneZoneButNotAnother()
    {
        var date = new DateOnly(2026, 7, 6);
        var start = Utc(2026, 7, 6, 13, 0); // 09:00 New York, 06:00 Los Angeles
        IReadOnlyList<ScheduleWindow> hours = [new(new(8, 0), new(17, 0))];

        Assert.Empty(_engine.Validate(Snapshot(NewYork, date, working: hours), start, start.AddMinutes(30)));
        Assert.Contains(_engine.Validate(Snapshot("America/Los_Angeles", date, working: hours), start, start.AddMinutes(30)),
            x => x.Contains("working hours", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(10, 7)]
    [InlineData(15, 5)]
    [InlineData(30, 3)]
    [InlineData(60, 2)]
    public void GenerateSlots_UsesTheClinicSlotInterval(int interval, int expectedSlots)
    {
        // 09:00–10:30 with a 30-minute service: the last possible start is 10:00.
        var snapshot = Snapshot("UTC", new DateOnly(2026, 7, 6), interval: interval, working: [new(new(9, 0), new(10, 30))]);

        var slots = _engine.GenerateSlots(snapshot, 30, LongAgo);

        Assert.Equal(expectedSlots, slots.Count);
        Assert.Equal(Utc(2026, 7, 6, 9, 0), slots[0].StartTimeUtc);
        Assert.Equal(interval, (slots[1].StartTimeUtc - slots[0].StartTimeUtc).TotalMinutes);
    }

    [Fact]
    public void Validate_OverlapIsBookedButBackToBackIsFine()
    {
        var date = new DateOnly(2026, 7, 6);
        var existing = new AppointmentWindow(Utc(2026, 7, 6, 13, 0), Utc(2026, 7, 6, 13, 30));
        var snapshot = Snapshot(NewYork, date, working: [new(new(8, 0), new(17, 0))], appointments: [existing]);

        Assert.Contains(_engine.Validate(snapshot, Utc(2026, 7, 6, 13, 15), Utc(2026, 7, 6, 13, 45)),
            x => x.Contains("booked", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(_engine.Validate(snapshot, Utc(2026, 7, 6, 12, 45), Utc(2026, 7, 6, 13, 15)),
            x => x.Contains("booked", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(_engine.Validate(snapshot, Utc(2026, 7, 6, 13, 30), Utc(2026, 7, 6, 14, 0)));
        Assert.Empty(_engine.Validate(snapshot, Utc(2026, 7, 6, 12, 30), Utc(2026, 7, 6, 13, 0)));
    }

    [Fact]
    public void Validate_DoctorOnLeave_IsRejected()
    {
        var snapshot = Snapshot(NewYork, new DateOnly(2026, 7, 6), leave: true, working: [new(new(8, 0), new(17, 0))]);

        Assert.Contains(_engine.Validate(snapshot, Utc(2026, 7, 6, 13, 0), Utc(2026, 7, 6, 13, 30)),
            x => x.Contains("leave", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_Reschedule_OnlyItsOwnOldTimeIsIgnored()
    {
        // The repository leaves the appointment being rescheduled out of the snapshot (excludeAppointmentId).
        var date = new DateOnly(2026, 7, 6);
        var own = new AppointmentWindow(Utc(2026, 7, 6, 13, 0), Utc(2026, 7, 6, 13, 30));
        var other = new AppointmentWindow(Utc(2026, 7, 6, 14, 0), Utc(2026, 7, 6, 14, 30));
        IReadOnlyList<ScheduleWindow> hours = [new(new(8, 0), new(17, 0))];
        var withoutOwn = Snapshot(NewYork, date, working: hours, appointments: [other]);

        Assert.Empty(_engine.Validate(withoutOwn, Utc(2026, 7, 6, 13, 15), Utc(2026, 7, 6, 13, 45)));
        Assert.NotEmpty(_engine.Validate(withoutOwn, Utc(2026, 7, 6, 13, 45), Utc(2026, 7, 6, 14, 15)));
        Assert.NotEmpty(_engine.Validate(
            Snapshot(NewYork, date, working: hours, appointments: [own, other]), Utc(2026, 7, 6, 13, 15), Utc(2026, 7, 6, 13, 45)));
    }

    private static void AssertNoOverlaps(IReadOnlyList<AvailableSlotDto> slots)
    {
        // Slots are sorted by start, and every start is a distinct instant.
        foreach (var (a, b) in slots.Zip(slots.Skip(1)))
        {
            Assert.True(a.StartTimeUtc < b.StartTimeUtc);
        }
    }

    private static SchedulingSnapshot Snapshot(
        string zone,
        DateOnly date,
        int interval = 15,
        bool leave = false,
        IReadOnlyList<ScheduleWindow>? working = null,
        IReadOnlyList<AppointmentWindow>? appointments = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), zone, date, working ?? [], [], leave, appointments ?? [], interval);

    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);
}
