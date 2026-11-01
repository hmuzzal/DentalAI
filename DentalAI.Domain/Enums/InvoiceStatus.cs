namespace DentalAI.Domain.Enums;

/// <summary>Draft (editable, no number) → Issued → PartiallyPaid → Paid. Void ends it (only with nothing paid).</summary>
public enum InvoiceStatus
{
    Draft = 0,
    Issued = 1,
    PartiallyPaid = 2,
    Paid = 3,
    Void = 4
}
