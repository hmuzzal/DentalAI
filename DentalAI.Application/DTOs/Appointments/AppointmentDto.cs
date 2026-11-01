using DentalAI.Domain.Enums;

namespace DentalAI.Application.DTOs.Appointments;

public sealed class AppointmentDto
{
    public Guid Id { get; set; }
    public Guid? ClinicId { get; set; }
    public Guid DoctorId { get; set; }
    public Guid PatientId { get; set; }
    public Guid ServiceId { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string? Notes { get; set; }
    public AppointmentStatus Status { get; set; }

    /// <summary>IANA time zone of the appointment's clinic; clients show times in it.</summary>
    public string? ClinicTimeZone { get; set; }
}
