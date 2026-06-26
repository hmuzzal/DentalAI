using DentalAI.Application.DTOs.Auth;
using FluentValidation;

namespace DentalAI.Application.Validators;

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.ClinicName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AdminEmail).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
        RuleFor(x => x.ClinicEmail).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.ClinicEmail));
    }
}
