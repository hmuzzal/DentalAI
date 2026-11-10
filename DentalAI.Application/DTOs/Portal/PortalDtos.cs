using DentalAI.Application.DTOs.Billing;
using DentalAI.Domain.Entities;
using DentalAI.Domain.Enums;

namespace DentalAI.Application.DTOs.Portal;

// ---- Public (no sign-in) ----

/// <summary>GET /portal/{slug}: what the sign-in page shows, and the clinic's booking rules.</summary>
public sealed record PortalClinicDto(
    string Name,
    string Slug,
    string? Phone,
    string? Address,
    string TimeZone,
    bool AllowNewPatients,
    int MinNoticeHours,
    int MaxDaysAhead,
    int CancelCutoffHours);

public sealed class PortalStartRequest
{
    /// <summary>An email address or a mobile number.</summary>
    public string Contact { get; set; } = string.Empty;
}

/// <summary>The same answer whether or not the contact is on file.</summary>
public sealed record PortalStartResponse(MessageChannel Channel, string Contact, int CodeValidMinutes);

public sealed class PortalVerifyRequest
{
    public string Contact { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}

/// <summary>A patient the verified contact belongs to (first name and last initial only).</summary>
public sealed record PortalPatientChoiceDto(Guid Id, string DisplayName);

/// <summary>
/// The code was right. <see cref="Ticket"/> (single use, 15 minutes) signs in as one of <see cref="Patients"/>, or
/// registers a new patient when <see cref="CanRegister"/>.
/// </summary>
public sealed record PortalVerifyResponse(
    string Ticket,
    DateTime TicketExpiresAtUtc,
    MessageChannel Channel,
    string Contact,
    IReadOnlyList<PortalPatientChoiceDto> Patients,
    bool CanRegister);

public sealed class PortalSelectRequest
{
    public string Ticket { get; set; } = string.Empty;
    public Guid PatientId { get; set; }
}

public sealed class PortalRegisterRequest
{
    public string Ticket { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
    /// <summary>Required when the code went to an email address; ignored when it went to a phone.</summary>
    public string? Phone { get; set; }
    /// <summary>Optional when the code went to a phone; ignored when it went to an email address.</summary>
    public string? Email { get; set; }
    public bool SmsOptIn { get; set; }
}

public sealed class PortalRefreshRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}

public sealed record PortalPatientSummaryDto(Guid Id, string FirstName, string LastName);

public sealed record PortalSessionDto(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    PortalPatientSummaryDto Patient,
    PortalClinicDto Clinic);

// ---- Signed-in patient ----

public sealed record PortalProfileDto(
    Guid Id,
    string FirstName,
    string LastName,
    DateOnly? DateOfBirth,
    string Phone,
    string? Email,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    ContactMethod PreferredContactMethod,
    bool SmsOptIn,
    PortalClinicDto Clinic);

/// <summary>What a patient may change themselves. Name and date of birth are changed by the clinic.</summary>
public sealed class PortalUpdateProfileRequest
{
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public ContactMethod PreferredContactMethod { get; set; } = ContactMethod.Phone;
    public bool SmsOptIn { get; set; }
}

public sealed record PortalServiceDto(Guid Id, string Name, string? Description, int DurationMinutes);

public sealed record PortalDoctorDto(Guid Id, string Name, string? Specialty);

public sealed record PortalSlotDto(DateTime StartTimeUtc, DateTime EndTimeUtc);

/// <summary>
/// No staff notes. <see cref="CanCancel"/> / <see cref="CanReschedule"/>: the patient may still do it online, until
/// <see cref="ChangeDeadlineUtc"/>. Rescheduling online also needs a service that is bookable online.
/// </summary>
public sealed record PortalAppointmentDto(
    Guid Id,
    Guid DoctorId,
    string DoctorName,
    Guid ServiceId,
    string ServiceName,
    DateTime StartTime,
    DateTime EndTime,
    AppointmentStatus Status,
    AppointmentSource Source,
    string TimeZone,
    bool CanCancel,
    bool CanReschedule,
    DateTime ChangeDeadlineUtc);

public sealed class PortalBookRequest
{
    public Guid DoctorId { get; set; }
    public Guid ServiceId { get; set; }
    /// <summary>UTC start, e.g. a slot's StartTimeUtc.</summary>
    public DateTime StartTime { get; set; }
    /// <summary>Optional note for the clinic (reason for the visit).</summary>
    public string? Note { get; set; }
}

public sealed class PortalRescheduleRequest
{
    public DateTime StartTime { get; set; }
}

public sealed record PortalInvoiceSummaryDto(
    Guid Id,
    string? Number,
    InvoiceStatus Status,
    DateOnly? IssueDate,
    DateOnly? DueDate,
    bool IsOverdue,
    string Currency,
    decimal Total,
    decimal AmountPaid,
    decimal Balance);

public sealed record PortalInvoiceListDto(
    IReadOnlyList<PortalInvoiceSummaryDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    IReadOnlyList<CurrencyAmountDto> Outstanding);

public sealed record PortalInvoiceLineDto(string Description, string? Tooth, int Quantity, decimal UnitPrice, decimal LineTotal);

public sealed record PortalPaymentDto(decimal Amount, PaymentMethod Method, DateTime ReceivedAtUtc, decimal RefundedAmount);

/// <summary>An invoice as the patient sees it: no staff names or internal payment notes.</summary>
public sealed record PortalInvoiceDto(
    Guid Id,
    string? Number,
    InvoiceStatus Status,
    DateOnly? IssueDate,
    DateOnly? DueDate,
    bool IsOverdue,
    string Currency,
    int CurrencyDecimals,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal TaxRatePercent,
    decimal TaxAmount,
    decimal Total,
    decimal InsuranceEstimate,
    decimal PatientResponsibility,
    decimal AmountPaid,
    decimal Balance,
    string? Notes,
    IReadOnlyList<PortalInvoiceLineDto> Lines,
    IReadOnlyList<PortalPaymentDto> Payments);

// ---- Clinic admins ----

/// <summary><see cref="Path"/> is the portal's address in the web app, e.g. "/p/smile-dental".</summary>
public sealed record PortalSettingsDto(
    bool Enabled,
    string Slug,
    string Path,
    bool AllowNewPatients,
    bool AutoConfirm,
    int MinNoticeHours,
    int MaxDaysAhead,
    int CancelCutoffHours,
    int MaxUpcomingAppointments);

public sealed class UpdatePortalSettingsRequest
{
    public bool Enabled { get; set; }
    public string Slug { get; set; } = string.Empty;
    public bool AllowNewPatients { get; set; } = true;
    public bool AutoConfirm { get; set; }
    public int MinNoticeHours { get; set; } = 2;
    public int MaxDaysAhead { get; set; } = 90;
    public int CancelCutoffHours { get; set; } = 24;
    public int MaxUpcomingAppointments { get; set; } = 3;
}
