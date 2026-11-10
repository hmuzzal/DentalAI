using DentalAI.Domain.Common;

namespace DentalAI.Domain.Entities;

public sealed class Service : BaseEntity, ITenantEntity
{
    public Guid? ClinicId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int DurationMinutes { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Patients can book this service on the clinic's portal.</summary>
    public bool BookableOnline { get; set; } = true;

    public Clinic? Clinic { get; set; }
    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
}
