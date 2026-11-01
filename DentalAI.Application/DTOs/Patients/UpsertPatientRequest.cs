using DentalAI.Domain.Enums;

namespace DentalAI.Application.DTOs.Patients;

public sealed class UpsertPatientRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    /// <summary>Any common format; stored as E.164 using the clinic's country for numbers without a country code.</summary>
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? Notes { get; set; }

    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }

    public ContactMethod PreferredContactMethod { get; set; } = ContactMethod.Phone;
    /// <summary>The server records when consent is given or withdrawn.</summary>
    public bool SmsOptIn { get; set; }

    /// <summary>
    /// The patient's policies: none, a primary, or a primary and a secondary. On update this replaces the list
    /// (send the existing ones to keep them). Null on update leaves insurance unchanged.
    /// </summary>
    public List<PatientInsuranceRequest>? Insurances { get; set; }
}

public sealed class PatientInsuranceRequest
{
    public InsurancePriority Priority { get; set; } = InsurancePriority.Primary;
    public string CarrierName { get; set; } = string.Empty;
    public string MemberId { get; set; } = string.Empty;
    public string? GroupNumber { get; set; }
    public InsuranceRelationship RelationshipToPatient { get; set; } = InsuranceRelationship.Self;
    /// <summary>Required unless the relationship is Self (then the patient is the subscriber).</summary>
    public string? SubscriberName { get; set; }
    public DateOnly? SubscriberDateOfBirth { get; set; }
}
