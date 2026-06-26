using DentalAI.Domain.Common;

namespace DentalAI.Domain.Entities;

public sealed class Patient : BaseEntity, ITenantEntity
{
    public Guid? ClinicId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? Notes { get; set; }

    public Clinic? Clinic { get; set; }
    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
}
