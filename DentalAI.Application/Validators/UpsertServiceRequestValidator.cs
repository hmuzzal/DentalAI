using DentalAI.Application.DTOs.Services;
using FluentValidation;

namespace DentalAI.Application.Validators;

public sealed class UpsertServiceRequestValidator : AbstractValidator<UpsertServiceRequest>
{
    public UpsertServiceRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
        RuleFor(x => x.DurationMinutes).InclusiveBetween(5, 480);
    }
}
