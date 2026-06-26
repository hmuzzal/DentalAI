using DentalAI.Domain.Common;

namespace DentalAI.Domain.Entities;

public sealed class Doctor : BaseEntity, ITenantEntity
{
    public Guid? ClinicId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Specialty { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    public Clinic? Clinic { get; set; }
    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
}
