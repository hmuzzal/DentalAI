using DentalAI.Domain.Common;

namespace DentalAI.Domain.Entities;

public sealed class DoctorBreak : BaseEntity, ITenantEntity
{
    public Guid? ClinicId { get; set; }
    public Guid DoctorId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public Doctor? Doctor { get; set; }
}
