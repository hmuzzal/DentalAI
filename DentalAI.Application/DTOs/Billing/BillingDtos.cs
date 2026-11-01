using DentalAI.Domain.Enums;

namespace DentalAI.Application.DTOs.Billing;

// ---- Settings ----

public sealed record BillingSettingsDto(
    string Currency,
    int CurrencyDecimals,
    string InvoicePrefix,
    int NextInvoiceNumber,
    int PaymentTermsDays,
    decimal DefaultTaxRatePercent,
    ToothNumbering ToothNumbering);

/// <summary>Clinic admins only. Changing the currency affects new invoices; existing ones keep theirs.</summary>
public sealed class UpdateBillingSettingsRequest
{
    public string Currency { get; set; } = "USD";
    public string InvoicePrefix { get; set; } = "INV";
    public int PaymentTermsDays { get; set; } = 30;
    public decimal DefaultTaxRatePercent { get; set; }
    public ToothNumbering ToothNumbering { get; set; } = ToothNumbering.Universal;
}

public sealed record CurrencyOptionDto(string Code, string Name, int Decimals);

// ---- Invoices ----

public sealed class InvoiceLineRequest
{
    public Guid? ServiceId { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? ProcedureCode { get; set; }
    public string? Tooth { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal InsuranceEstimate { get; set; }
}

/// <summary>Creates a draft. Tax rate defaults to the clinic's; currency is always the clinic's current one.</summary>
public sealed class CreateInvoiceRequest
{
    public Guid PatientId { get; set; }
    public Guid? AppointmentId { get; set; }
    public List<InvoiceLineRequest> Lines { get; set; } = [];
    public decimal DiscountAmount { get; set; }
    public decimal? TaxRatePercent { get; set; }
    /// <summary>Optional: sets every line's insurance estimate to this % of the line (e.g. 80 for 80% coverage).</summary>
    public decimal? InsuranceCoveragePercent { get; set; }
    public string? Notes { get; set; }
}

/// <summary>Replaces a draft's lines and amounts. Issued invoices can't be edited.</summary>
public sealed class UpdateInvoiceRequest
{
    public List<InvoiceLineRequest> Lines { get; set; } = [];
    public decimal DiscountAmount { get; set; }
    public decimal TaxRatePercent { get; set; }
    public decimal? InsuranceCoveragePercent { get; set; }
    public string? Notes { get; set; }
}

public sealed class RecordPaymentRequest
{
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public string? Reference { get; set; }
    /// <summary>Defaults to now. Can't be in the future.</summary>
    public DateTime? ReceivedAtUtc { get; set; }
    public string? Note { get; set; }
}

public sealed class RefundRequest
{
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class VoidInvoiceRequest
{
    public string Reason { get; set; } = string.Empty;
}

public sealed class InvoiceListQuery
{
    public const int MaxPageSize = 100;
    public InvoiceStatus? Status { get; set; }
    public Guid? PatientId { get; set; }
    /// <summary>The invoice for an appointment (at most one that isn't void).</summary>
    public Guid? AppointmentId { get; set; }
    /// <summary>Only invoices with money still owed (Issued or PartiallyPaid).</summary>
    public bool OutstandingOnly { get; set; }
    /// <summary>Matches the invoice number or the patient's name.</summary>
    public string? Search { get; set; }
    /// <summary>Issue date range (drafts: creation date), clinic-local.</summary>
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed record InvoiceSummaryDto(
    Guid Id,
    string? Number,
    InvoiceStatus Status,
    Guid PatientId,
    string PatientName,
    Guid? AppointmentId,
    DateOnly? IssueDate,
    DateOnly? DueDate,
    bool IsOverdue,
    string Currency,
    decimal Total,
    decimal AmountPaid,
    decimal Balance,
    DateTime CreatedDate);

/// <summary>Outstanding balance per currency (a clinic that changed currency can have both).</summary>
public sealed record CurrencyAmountDto(string Currency, decimal Amount);

public sealed record InvoiceListDto(
    IReadOnlyList<InvoiceSummaryDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    IReadOnlyList<CurrencyAmountDto> Outstanding);

public sealed record InvoiceLineDto(
    Guid Id,
    Guid? ServiceId,
    string Description,
    string? ProcedureCode,
    string? Tooth,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    decimal InsuranceEstimate);

public sealed record RefundDto(Guid Id, decimal Amount, string Reason, DateTime RefundedAtUtc, string? RefundedBy);

public sealed record PaymentDto(
    Guid Id,
    decimal Amount,
    PaymentMethod Method,
    string? Reference,
    DateTime ReceivedAtUtc,
    string? Note,
    decimal RefundedAmount,
    string? RecordedBy,
    IReadOnlyList<RefundDto> Refunds);

public sealed record InvoiceDto(
    Guid Id,
    string? Number,
    InvoiceStatus Status,
    Guid PatientId,
    string PatientName,
    Guid? AppointmentId,
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
    string? VoidReason,
    DateTime? VoidedAtUtc,
    DateTime CreatedDate,
    IReadOnlyList<InvoiceLineDto> Lines,
    IReadOnlyList<PaymentDto> Payments);

/// <summary>Who an invoice is from or to, for the printed invoice.</summary>
public sealed record InvoicePartyDto(string Name, string? AddressLine1, string? AddressLine2, string? CityLine, string? Phone, string? Email);

/// <summary>GET /invoices/{id}/document: everything needed to print or send an invoice.</summary>
public sealed record InvoiceDocumentDto(InvoiceDto Invoice, InvoicePartyDto Clinic, InvoicePartyDto Patient, ToothNumbering ToothNumbering);
