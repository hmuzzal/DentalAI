using DentalAI.Application.Common.Models;
using DentalAI.Application.Services;
using Xunit;

namespace DentalAI.Application.Tests.Services;

public sealed class SchedulingEngineTests
{
    private readonly SchedulingEngine _engine = new();
    private static readonly DateOnly Monday = new(2026, 7, 6);

    [Fact]
    public void GenerateSlots_UsesServiceDurationAndFifteenMinuteGrid()
    {
        var snapshot = Snapshot(working: [new(new TimeOnly(9, 0), new TimeOnly(10, 0))]);

        var slots = _engine.GenerateSlots(snapshot, 30, new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(3, slots.Count);
        Assert.Equal(new DateTime(2026, 7, 6, 9, 0, 0, DateTimeKind.Utc), slots[0].StartTimeUtc);
        Assert.Equal(new DateTime(2026, 7, 6, 9, 30, 0, DateTimeKind.Utc), slots[0].EndTimeUtc);
    }

    [Fact]
    public void GenerateSlots_ExcludesBreaksAndAppointments()
    {
        var snapshot = Snapshot(
            working: [new(new TimeOnly(9, 0), new TimeOnly(12, 0))],
            breaks: [new(new TimeOnly(10, 0), new TimeOnly(10, 30))],
            appointments: [new(Utc(11, 0), Utc(11, 30))]);

        var slots = _engine.GenerateSlots(snapshot, 30, Utc(0, 0));

        Assert.DoesNotContain(slots, x => x.StartTimeUtc == Utc(10, 0) || x.StartTimeUtc == Utc(11, 0));
    }

    [Fact]
    public void GenerateSlots_ReturnsNoneWhenDoctorIsOnLeave()
    {
        var slots = _engine.GenerateSlots(Snapshot(true, [new(new TimeOnly(9, 0), new TimeOnly(17, 0))]), 30, Utc(0, 0));
        Assert.Empty(slots);
    }

    [Fact]
    public void Validate_ReturnsDescriptiveErrorsForInvalidBooking()
    {
        var snapshot = Snapshot(
            working: [new(new TimeOnly(9, 0), new TimeOnly(17, 0))],
            breaks: [new(new TimeOnly(13, 0), new TimeOnly(14, 0))]);

        var errors = _engine.Validate(snapshot, Utc(13, 15), Utc(13, 45));

        Assert.Contains(errors, x => x.Contains("break", StringComparison.OrdinalIgnoreCase));
    }

    private static SchedulingSnapshot Snapshot(
        bool leave = false,
        IReadOnlyList<ScheduleWindow>? working = null,
        IReadOnlyList<ScheduleWindow>? breaks = null,
        IReadOnlyList<AppointmentWindow>? appointments = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "UTC", Monday, working ?? [], breaks ?? [], leave, appointments ?? []);

    private static DateTime Utc(int hour, int minute) => new(2026, 7, 6, hour, minute, 0, DateTimeKind.Utc);
}
