using DentalAI.Domain.Common;

namespace DentalAI.Domain.Entities;

/// <summary>
/// A clinic's patient portal (one row per clinic), managed by its admins. Created on first use, switched off.
/// </summary>
public sealed class ClinicPortalSettings : BaseEntity, ITenantEntity
{
    public Guid? ClinicId { get; set; }
    public bool Enabled { get; set; }
    /// <summary>The portal's address: /p/{slug}. Lowercase letters, digits and dashes; unique across clinics.</summary>
    public string Slug { get; set; } = string.Empty;
    /// <summary>People who aren't patients yet may register after verifying their email or phone.</summary>
    public bool AllowNewPatients { get; set; } = true;
    /// <summary>Online bookings are Confirmed straight away; otherwise Pending until the front desk confirms.</summary>
    public bool AutoConfirm { get; set; }
    /// <summary>The earliest bookable time is this many hours from now.</summary>
    public int MinNoticeHours { get; set; } = 2;
    /// <summary>How many days ahead patients can book.</summary>
    public int MaxDaysAhead { get; set; } = 90;
    /// <summary>Patients can cancel or reschedule online until this many hours before the appointment.</summary>
    public int CancelCutoffHours { get; set; } = 24;
    /// <summary>A patient can have at most this many upcoming (pending or confirmed) appointments when booking online.</summary>
    public int MaxUpcomingAppointments { get; set; } = 3;
}

/// <summary>
/// A one-time sign-in code sent to a patient's email or phone. Only the hash is stored. Once verified it becomes a
/// short-lived ticket used to choose a patient or register.
/// </summary>
public sealed class PortalSignInCode : BaseEntity, ITenantEntity
{
    public Guid? ClinicId { get; set; }
    public MessageChannel Channel { get; set; }
    /// <summary>Lowercase email or E.164 phone number.</summary>
    public string Contact { get; set; } = string.Empty;
    public string CodeHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public int FailedAttempts { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }
    /// <summary>SHA-256 of the ticket returned on verification.</summary>
    public string? TicketHash { get; set; }
    public DateTime? TicketExpiresAtUtc { get; set; }
    /// <summary>Set when the ticket has been used to sign in or register.</summary>
    public DateTime? UsedAtUtc { get; set; }
}

/// <summary>A signed-in patient's refresh token (hashed). Rotated on every refresh; revoked on sign-out.</summary>
public sealed class PatientPortalSession : BaseEntity, ITenantEntity
{
    public Guid? ClinicId { get; set; }
    public Guid PatientId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public DateTime? LastUsedAtUtc { get; set; }

    public Patient? Patient { get; set; }
}
