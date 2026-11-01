using DentalAI.Application.Common.Billing;
using DentalAI.Domain.Entities;
using DentalAI.Domain.Enums;
using Xunit;

namespace DentalAI.Application.Tests.Services;

public sealed class CurrencyRulesTests
{
    [Theory]
    [InlineData("US", "USD")]
    [InlineData("BD", "BDT")]
    [InlineData("GB", "GBP")]
    [InlineData("JP", "JPY")]
    [InlineData("DE", "EUR")]
    [InlineData(null, "USD")]
    [InlineData("XX", "USD")]
    public void ForCountry_GivesTheLocalCurrency(string? country, string expected) =>
        Assert.Equal(expected, CurrencyRules.ForCountry(country));

    [Theory]
    [InlineData("USD", true)]
    [InlineData("bdt", true)]
    [InlineData("EUR", true)]
    [InlineData("ABC", false)]
    [InlineData("US", false)]
    [InlineData(null, false)]
    public void IsKnown_AcceptsIso4217Codes(string? code, bool known) =>
        Assert.Equal(known, CurrencyRules.IsKnown(code));

    [Theory]
    [InlineData("USD", 10.005, 10.01)]
    [InlineData("BDT", 99.994, 99.99)]
    [InlineData("JPY", 1234.5, 1235)]
    [InlineData("KWD", 1.2345, 1.235)]
    public void Round_UsesTheCurrencysDecimals(string currency, decimal amount, decimal expected) =>
        Assert.Equal(expected, CurrencyRules.Round(amount, currency));

    [Fact]
    public void All_ListsUsDollarFirst()
    {
        var all = CurrencyRules.All();
        Assert.Equal("USD", all[0].Code);
        Assert.Contains(all, c => c.Code == "BDT");
        Assert.Equal(all.Count, all.Select(c => c.Code).Distinct().Count());
    }
}

public sealed class InvoiceCalculatorTests
{
    [Fact]
    public void Totals_LinesDiscountTaxAndInsurance()
    {
        var invoice = Issued("USD",
            Line(2, 75m, insurance: 120m),   // 150.00, insurer expected to pay 120
            Line(1, 100m));                  // 100.00
        invoice.DiscountAmount = 25m;
        invoice.TaxRatePercent = 8.875m;

        InvoiceCalculator.Recalculate(invoice);

        Assert.Equal(250m, invoice.Subtotal);
        Assert.Equal(19.97m, invoice.TaxAmount);         // 225 × 8.875% = 19.96875
        Assert.Equal(244.97m, invoice.Total);
        Assert.Equal(120m, invoice.InsuranceEstimate);
        Assert.Equal(124.97m, invoice.PatientResponsibility);
        Assert.Equal(244.97m, invoice.Balance);
        Assert.Equal(InvoiceStatus.Issued, invoice.Status);
    }

    [Fact]
    public void Status_FollowsPaymentsAndRefunds()
    {
        var invoice = Issued("USD", Line(1, 100m));
        var payment = new Payment { Amount = 40m };
        invoice.Payments.Add(payment);
        InvoiceCalculator.Recalculate(invoice);
        Assert.Equal(InvoiceStatus.PartiallyPaid, invoice.Status);
        Assert.Equal(60m, invoice.Balance);

        invoice.Payments.Add(new Payment { Amount = 60m });
        InvoiceCalculator.Recalculate(invoice);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(0m, invoice.Balance);

        payment.Refunds.Add(new Refund { Amount = 15m });
        InvoiceCalculator.Recalculate(invoice);
        Assert.Equal(15m, payment.RefundedAmount);
        Assert.Equal(85m, invoice.AmountPaid);
        Assert.Equal(InvoiceStatus.PartiallyPaid, invoice.Status);
    }

    [Fact]
    public void DraftsAndVoidsKeepTheirStatus()
    {
        var draft = Issued("USD", Line(1, 50m));
        draft.Status = InvoiceStatus.Draft;
        InvoiceCalculator.Recalculate(draft);
        Assert.Equal(InvoiceStatus.Draft, draft.Status);

        var voided = Issued("USD", Line(1, 50m));
        voided.Status = InvoiceStatus.Void;
        InvoiceCalculator.Recalculate(voided);
        Assert.Equal(InvoiceStatus.Void, voided.Status);
    }

    [Fact]
    public void ZeroTotalInvoice_IsPaidWhenIssued()
    {
        var invoice = Issued("USD", Line(1, 0m));
        InvoiceCalculator.Recalculate(invoice);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
    }

    [Fact]
    public void DiscountAndInsurance_AreCappedAndDeletedLinesIgnored()
    {
        var removed = Line(1, 500m);
        removed.IsDeleted = true;
        var invoice = Issued("USD", Line(1, 100m, insurance: 999m), removed);
        invoice.DiscountAmount = 1000m;

        InvoiceCalculator.Recalculate(invoice);

        Assert.Equal(100m, invoice.Subtotal);
        Assert.Equal(100m, invoice.DiscountAmount);
        Assert.Equal(0m, invoice.Total);
        Assert.Equal(0m, invoice.InsuranceEstimate);
    }

    [Fact]
    public void Amounts_RoundToTheInvoiceCurrency()
    {
        var yen = Issued("JPY", Line(3, 333.4m));
        yen.TaxRatePercent = 10m;
        InvoiceCalculator.Recalculate(yen);
        Assert.Equal(1000m, yen.Subtotal);   // 1000.2 → 1000
        Assert.Equal(100m, yen.TaxAmount);
        Assert.Equal(1100m, yen.Total);
    }

    private static Invoice Issued(string currency, params InvoiceLine[] lines)
    {
        var invoice = new Invoice { Currency = currency, Status = InvoiceStatus.Issued };
        foreach (var line in lines) invoice.Lines.Add(line);
        return invoice;
    }

    private static InvoiceLine Line(int quantity, decimal unitPrice, decimal insurance = 0m) =>
        new() { Description = "Procedure", Quantity = quantity, UnitPrice = unitPrice, InsuranceEstimate = insurance };
}
