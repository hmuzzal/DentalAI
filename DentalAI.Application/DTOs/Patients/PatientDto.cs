using DentalAI.Domain.Enums;

namespace DentalAI.Application.DTOs.Patients;

public sealed class PatientDto
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    /// <summary>E.164, e.g. +12125550142 (older records may not be normalised yet).</summary>
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

    public ContactMethod PreferredContactMethod { get; set; }
    public bool SmsOptIn { get; set; }
    public DateTime? SmsOptInAtUtc { get; set; }
    public DateTime? SmsOptOutAtUtc { get; set; }

    /// <summary>Filled by GET /patients/{id}; lists leave it empty.</summary>
    public IReadOnlyList<PatientInsuranceDto> Insurances { get; set; } = [];
}

public sealed class PatientInsuranceDto
{
    public Guid Id { get; set; }
    public InsurancePriority Priority { get; set; }
    public string CarrierName { get; set; } = string.Empty;
    public string MemberId { get; set; } = string.Empty;
    public string? GroupNumber { get; set; }
    public InsuranceRelationship RelationshipToPatient { get; set; }
    public string SubscriberName { get; set; } = string.Empty;
    public DateOnly? SubscriberDateOfBirth { get; set; }
}
