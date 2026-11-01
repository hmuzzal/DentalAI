using DentalAI.Domain.Common;
using DentalAI.Domain.Enums;

namespace DentalAI.Domain.Entities;

/// <summary>A dental insurance policy covering a patient. A patient has at most one primary and one secondary.</summary>
public sealed class PatientInsurance : BaseEntity, ITenantEntity
{
    public Guid? ClinicId { get; set; }
    public Guid PatientId { get; set; }
    public InsurancePriority Priority { get; set; } = InsurancePriority.Primary;

    /// <summary>Insurance company, e.g. "Delta Dental".</summary>
    public string CarrierName { get; set; } = string.Empty;
    public string MemberId { get; set; } = string.Empty;
    public string? GroupNumber { get; set; }

    public InsuranceRelationship RelationshipToPatient { get; set; } = InsuranceRelationship.Self;
    /// <summary>The policy holder. Same as the patient when the relationship is Self.</summary>
    public string SubscriberName { get; set; } = string.Empty;
    public DateOnly? SubscriberDateOfBirth { get; set; }

    public Patient? Patient { get; set; }
}
