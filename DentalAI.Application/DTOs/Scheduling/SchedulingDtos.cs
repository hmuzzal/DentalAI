namespace DentalAI.Application.DTOs.Scheduling;

public sealed record DoctorScheduleDto(Guid Id, DayOfWeek DayOfWeek, TimeOnly StartTime, TimeOnly EndTime, bool IsWorking);
public sealed record DoctorBreakDto(Guid Id, DayOfWeek DayOfWeek, TimeOnly StartTime, TimeOnly EndTime);
public sealed record DoctorLeaveDto(Guid Id, DateOnly StartDate, DateOnly EndDate, string Reason);

public sealed class SaveDoctorAvailabilityRequest
{
    public IReadOnlyList<SchedulePeriodRequest> Schedules { get; set; } = [];
    public IReadOnlyList<BreakPeriodRequest> Breaks { get; set; } = [];
    public IReadOnlyList<LeavePeriodRequest> Leaves { get; set; } = [];
}

public sealed record SchedulePeriodRequest(DayOfWeek DayOfWeek, TimeOnly StartTime, TimeOnly EndTime, bool IsWorking = true);
public sealed record BreakPeriodRequest(DayOfWeek DayOfWeek, TimeOnly StartTime, TimeOnly EndTime);
public sealed record LeavePeriodRequest(DateOnly StartDate, DateOnly EndDate, string Reason);
public sealed record DoctorAvailabilityDto(
    Guid DoctorId,
    IReadOnlyList<DoctorScheduleDto> Schedules,
    IReadOnlyList<DoctorBreakDto> Breaks,
    IReadOnlyList<DoctorLeaveDto> Leaves);

public sealed record AvailableSlotDto(DateTime StartTimeUtc, DateTime EndTimeUtc);
