using DentalAI.Domain.Common;
using DentalAI.Domain.Enums;

namespace DentalAI.Domain.Entities;

/// <summary>
/// A clinic's billing settings (one row per clinic), managed by its admins. Created on first use with defaults
/// from the clinic's country.
/// </summary>
public sealed class ClinicBillingSettings : BaseEntity, ITenantEntity
{
    public Guid? ClinicId { get; set; }
    /// <summary>ISO 4217 code, e.g. "USD" or "BDT". New invoices use it; existing invoices keep their own.</summary>
    public string Currency { get; set; } = "USD";
    /// <summary>Invoice numbers look like "{Prefix}-000123".</summary>
    public string InvoicePrefix { get; set; } = "INV";
    /// <summary>The next number to give an issued invoice. Only ever increased, under a lock.</summary>
    public int NextInvoiceNumber { get; set; } = 1;
    /// <summary>Days from issue to due date (0 = due on receipt).</summary>
    public int PaymentTermsDays { get; set; } = 30;
    /// <summary>Applied to new invoices (0 by default: US dental services are generally not taxed).</summary>
    public decimal DefaultTaxRatePercent { get; set; }
    public ToothNumbering ToothNumbering { get; set; } = ToothNumbering.Universal;
}

/// <summary>
/// A bill for a patient. Drafts are editable and unnumbered. Once issued, an invoice changes only through payments,
/// refunds or voiding; its amounts are recalculated from its lines and payments by <c>InvoiceCalculator</c>.
/// </summary>
public sealed class Invoice : BaseEntity, ITenantEntity
{
    public Guid? ClinicId { get; set; }
    public Guid PatientId { get; set; }
    /// <summary>The visit this bills, when created from an appointment.</summary>
    public Guid? AppointmentId { get; set; }

    /// <summary>Null while a draft; set when issued (e.g. "INV-000123"), unique per clinic.</summary>
    public string? Number { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
    public DateOnly? IssueDate { get; set; }
    public DateOnly? DueDate { get; set; }
    /// <summary>ISO 4217; fixed when the invoice is created.</summary>
    public string Currency { get; set; } = "USD";

    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxRatePercent { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    /// <summary>What the clinic expects insurance to pay (an estimate, not a claim).</summary>
    public decimal InsuranceEstimate { get; set; }
    /// <summary>Total minus the insurance estimate: what the patient is expected to pay.</summary>
    public decimal PatientResponsibility { get; set; }
    /// <summary>Payments minus refunds.</summary>
    public decimal AmountPaid { get; set; }
    /// <summary>Total minus amount paid.</summary>
    public decimal Balance { get; set; }

    public string? Notes { get; set; }
    public string? VoidReason { get; set; }
    public DateTime? VoidedAtUtc { get; set; }

    public Patient? Patient { get; set; }
    public Appointment? Appointment { get; set; }
    public ICollection<InvoiceLine> Lines { get; set; } = new List<InvoiceLine>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}

public sealed class InvoiceLine : BaseEntity, ITenantEntity
{
    public Guid? ClinicId { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid? ServiceId { get; set; }
    public int SortOrder { get; set; }
    public string Description { get; set; } = string.Empty;
    /// <summary>Optional procedure code, e.g. a CDT code ("D1110") for US insurance.</summary>
    public string? ProcedureCode { get; set; }
    /// <summary>Optional tooth, in the clinic's numbering system (Universal "14" or FDI "26").</summary>
    public string? Tooth { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public decimal InsuranceEstimate { get; set; }

    public Invoice? Invoice { get; set; }
}

/// <summary>Money received against an invoice. Corrections are refunds, never edits.</summary>
public sealed class Payment : BaseEntity, ITenantEntity
{
    public Guid? ClinicId { get; set; }
    public Guid InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    /// <summary>Check number, card last 4, insurer reference, Stripe ID, ...</summary>
    public string? Reference { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public string? Note { get; set; }
    /// <summary>Sum of this payment's refunds.</summary>
    public decimal RefundedAmount { get; set; }

    public Invoice? Invoice { get; set; }
    public ICollection<Refund> Refunds { get; set; } = new List<Refund>();
}

public sealed class Refund : BaseEntity, ITenantEntity
{
    public Guid? ClinicId { get; set; }
    public Guid PaymentId { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime RefundedAtUtc { get; set; }

    public Payment? Payment { get; set; }
}
