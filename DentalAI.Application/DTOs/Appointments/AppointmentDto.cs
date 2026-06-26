using DentalAI.Domain.Enums;

namespace DentalAI.Application.DTOs.Appointments;

public sealed class AppointmentDto
{
    public Guid Id { get; set; }
    public Guid? ClinicId { get; set; }
    public Guid DoctorId { get; set; }
    public Guid PatientId { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string? Notes { get; set; }
    public AppointmentStatus Status { get; set; }
}
