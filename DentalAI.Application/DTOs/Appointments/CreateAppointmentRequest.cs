namespace DentalAI.Application.DTOs.Appointments;

public sealed class CreateAppointmentRequest
{
    public Guid DoctorId { get; set; }
    public Guid PatientId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string? Notes { get; set; }
}
