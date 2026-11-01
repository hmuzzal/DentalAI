using DentalAI.Domain.Common;

namespace DentalAI.Domain.Entities;

public sealed class DoctorLeave : BaseEntity, ITenantEntity
{
    public Guid? ClinicId { get; set; }
    public Guid DoctorId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Doctor? Doctor { get; set; }
}
