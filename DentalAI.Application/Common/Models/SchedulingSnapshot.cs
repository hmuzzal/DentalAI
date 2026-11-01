namespace DentalAI.Application.Common.Models;

public sealed record ScheduleWindow(TimeOnly Start, TimeOnly End);
public sealed record AppointmentWindow(DateTime StartUtc, DateTime EndUtc);

public sealed record SchedulingSnapshot(
    Guid DoctorId,
    Guid? ClinicId,
    string TimeZoneId,
    DateOnly Date,
    IReadOnlyList<ScheduleWindow> WorkingPeriods,
    IReadOnlyList<ScheduleWindow> BreakPeriods,
    bool IsOnLeave,
    IReadOnlyList<AppointmentWindow> Appointments);
