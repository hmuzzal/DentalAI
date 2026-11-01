using DentalAI.Application.Common.Billing;
using DentalAI.Application.DTOs.Billing;
using FluentValidation;

namespace DentalAI.Application.Validators;

public sealed class UpdateBillingSettingsRequestValidator : AbstractValidator<UpdateBillingSettingsRequest>
{
    public UpdateBillingSettingsRequestValidator()
    {
        RuleFor(x => x.Currency).NotEmpty().Must(CurrencyRules.IsKnown)
            .WithMessage("Currency must be a 3-letter ISO 4217 code, e.g. USD or BDT.");
        RuleFor(x => x.InvoicePrefix).NotEmpty().MaximumLength(10)
            .Matches("^[A-Za-z0-9]+$").WithMessage("Invoice prefix can only contain letters and numbers.");
        RuleFor(x => x.PaymentTermsDays).InclusiveBetween(0, 365);
        RuleFor(x => x.DefaultTaxRatePercent).InclusiveBetween(0, 100);
        RuleFor(x => x.ToothNumbering).IsInEnum();
    }
}

public sealed class InvoiceLineRequestValidator : AbstractValidator<InvoiceLineRequest>
{
    public InvoiceLineRequestValidator()
    {
        RuleFor(x => x.Description).NotEmpty().WithMessage("Each line needs a description.").MaximumLength(300);
        RuleFor(x => x.ProcedureCode).MaximumLength(20);
        RuleFor(x => x.Tooth).MaximumLength(20);
        RuleFor(x => x.Quantity).InclusiveBetween(1, 1000);
        RuleFor(x => x.UnitPrice).InclusiveBetween(0, 10_000_000).WithMessage("Price must be 0 or more.");
        RuleFor(x => x.InsuranceEstimate).GreaterThanOrEqualTo(0)
            .LessThanOrEqualTo(x => x.Quantity * x.UnitPrice).WithMessage("The insurance estimate can't be more than the line total.");
    }
}

public sealed class CreateInvoiceRequestValidator : AbstractValidator<CreateInvoiceRequest>
{
    public CreateInvoiceRequestValidator()
    {
        RuleFor(x => x.PatientId).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Add at least one line.").Must(x => x.Count <= 100);
        RuleForEach(x => x.Lines).SetValidator(new InvoiceLineRequestValidator());
        RuleFor(x => x.DiscountAmount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.TaxRatePercent).InclusiveBetween(0, 100).When(x => x.TaxRatePercent.HasValue);
        RuleFor(x => x.InsuranceCoveragePercent).InclusiveBetween(0, 100).When(x => x.InsuranceCoveragePercent.HasValue);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public sealed class UpdateInvoiceRequestValidator : AbstractValidator<UpdateInvoiceRequest>
{
    public UpdateInvoiceRequestValidator()
    {
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Add at least one line.").Must(x => x.Count <= 100);
        RuleForEach(x => x.Lines).SetValidator(new InvoiceLineRequestValidator());
        RuleFor(x => x.DiscountAmount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.TaxRatePercent).InclusiveBetween(0, 100);
        RuleFor(x => x.InsuranceCoveragePercent).InclusiveBetween(0, 100).When(x => x.InsuranceCoveragePercent.HasValue);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public sealed class RecordPaymentRequestValidator : AbstractValidator<RecordPaymentRequest>
{
    public RecordPaymentRequestValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("The amount must be more than 0.");
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.Reference).MaximumLength(100);
        RuleFor(x => x.Note).MaximumLength(500);
    }
}

public sealed class RefundRequestValidator : AbstractValidator<RefundRequest>
{
    public RefundRequestValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("The amount must be more than 0.");
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Enter a reason for the refund.").MaximumLength(500);
    }
}

public sealed class VoidInvoiceRequestValidator : AbstractValidator<VoidInvoiceRequest>
{
    public VoidInvoiceRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Enter a reason for voiding the invoice.").MaximumLength(500);
    }
}
