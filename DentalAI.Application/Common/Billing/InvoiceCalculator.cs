using DentalAI.Domain.Entities;
using DentalAI.Domain.Enums;

namespace DentalAI.Application.Common.Billing;

/// <summary>
/// The one place invoice amounts and status are worked out, from lines, discount, tax rate, payments and refunds.
/// Every change to an invoice ends by calling <see cref="Recalculate"/>, so stored totals never drift from the parts.
/// </summary>
public static class InvoiceCalculator
{
    public static void Recalculate(Invoice invoice)
    {
        var currency = invoice.Currency;
        // Removed lines are soft-deleted but stay in the collection until saved.
        var lines = invoice.Lines.Where(x => !x.IsDeleted).ToList();
        foreach (var line in lines)
        {
            line.LineTotal = CurrencyRules.Round(line.Quantity * line.UnitPrice, currency);
            line.InsuranceEstimate = Math.Clamp(CurrencyRules.Round(line.InsuranceEstimate, currency), 0, line.LineTotal);
        }

        invoice.Subtotal = lines.Sum(x => x.LineTotal);
        invoice.DiscountAmount = Math.Clamp(CurrencyRules.Round(invoice.DiscountAmount, currency), 0, invoice.Subtotal);
        var taxable = invoice.Subtotal - invoice.DiscountAmount;
        invoice.TaxAmount = CurrencyRules.Round(taxable * invoice.TaxRatePercent / 100m, currency);
        invoice.Total = taxable + invoice.TaxAmount;

        invoice.InsuranceEstimate = Math.Min(lines.Sum(x => x.InsuranceEstimate), invoice.Total);
        invoice.PatientResponsibility = invoice.Total - invoice.InsuranceEstimate;

        foreach (var payment in invoice.Payments)
        {
            payment.RefundedAmount = payment.Refunds.Where(r => !r.IsDeleted).Sum(r => r.Amount);
        }
        invoice.AmountPaid = invoice.Payments.Where(p => !p.IsDeleted).Sum(p => p.Amount - p.RefundedAmount);
        invoice.Balance = invoice.Total - invoice.AmountPaid;

        invoice.Status = StatusOf(invoice);
    }

    private static InvoiceStatus StatusOf(Invoice invoice) => invoice.Status switch
    {
        InvoiceStatus.Draft or InvoiceStatus.Void => invoice.Status,
        _ when invoice.Balance <= 0 => InvoiceStatus.Paid,
        _ when invoice.AmountPaid > 0 => InvoiceStatus.PartiallyPaid,
        _ => InvoiceStatus.Issued
    };
}
