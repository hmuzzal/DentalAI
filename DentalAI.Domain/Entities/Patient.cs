using DentalAI.Domain.Common;
using DentalAI.Domain.Enums;

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

    // Mailing address (US-style: state is the 2-letter code, postal code the ZIP).
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }

    public ContactMethod PreferredContactMethod { get; set; } = ContactMethod.Phone;

    /// <summary>Consent to receive SMS (TCPA). The timestamps record when consent was last given or withdrawn.</summary>
    public bool SmsOptIn { get; set; }
    public DateTime? SmsOptInAtUtc { get; set; }
    public DateTime? SmsOptOutAtUtc { get; set; }

    public Clinic? Clinic { get; set; }
    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
    public ICollection<PatientInsurance> Insurances { get; set; } = new List<PatientInsurance>();
}
