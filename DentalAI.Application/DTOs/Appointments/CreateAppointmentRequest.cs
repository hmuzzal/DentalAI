namespace DentalAI.Application.DTOs.Appointments;

public sealed class CreateAppointmentRequest
{
    public Guid DoctorId { get; set; }
    public Guid PatientId { get; set; }
    public Guid ServiceId { get; set; }
    public DateTime StartTime { get; set; }
    public string? Notes { get; set; }
}
