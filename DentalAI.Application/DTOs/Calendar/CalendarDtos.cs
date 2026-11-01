using DentalAI.Domain.Enums;

namespace DentalAI.Application.DTOs.Calendar;

public sealed record CalendarQuery(Guid? ClinicId, Guid? DoctorId, DateOnly Date);

public sealed record CalendarAppointmentDto(
    Guid Id,
    Guid? ClinicId,
    Guid DoctorId,
    string DoctorName,
    Guid PatientId,
    string PatientName,
    Guid ServiceId,
    string ServiceName,
    DateTime StartTimeUtc,
    DateTime EndTimeUtc,
    AppointmentStatus Status,
    /// <summary>IANA time zone of the appointment's clinic; clients show times in it.</summary>
    string? ClinicTimeZone);

public sealed record CalendarDto(DateOnly FromDate, DateOnly ToDate, IReadOnlyList<CalendarAppointmentDto> Appointments);
