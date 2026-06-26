using DentalAI.Domain.Common;
using DentalAI.Domain.Enums;

namespace DentalAI.Domain.Entities;

public sealed class Appointment : BaseEntity, ITenantEntity
{
    public Guid? ClinicId { get; set; }
    public Guid DoctorId { get; set; }
    public Guid PatientId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string? Notes { get; set; }
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Pending;

    public Clinic? Clinic { get; set; }
    public Doctor? Doctor { get; set; }
    public Patient? Patient { get; set; }
}
