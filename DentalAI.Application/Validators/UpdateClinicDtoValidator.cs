using DentalAI.Application.DTOs.Clinics;
using FluentValidation;

namespace DentalAI.Application.Validators;

public sealed class UpdateClinicDtoValidator : AbstractValidator<UpdateClinicDto>
{
    private const string PhonePattern = @"^\+?[0-9\s\-().]{7,20}$";

    public UpdateClinicDtoValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Phone).NotEmpty().Matches(PhonePattern);
        RuleFor(x => x.Country).NotEmpty().Must(ClinicRules.IsKnownCountry)
            .WithMessage("Country must be a 2-letter country code, e.g. US.");
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(x => x.Address).NotEmpty().MaximumLength(500);
        RuleFor(x => x.TimeZone).NotEmpty().MaximumLength(100)
            .Must(ClinicRules.IsKnownTimeZone).WithMessage("Time zone must be a valid IANA time-zone ID, e.g. America/New_York.");
        RuleFor(x => x.SlotIntervalMinutes).Must(ClinicRules.IsAllowedSlotInterval)
            .WithMessage($"Slot interval must be one of: {string.Join(", ", ClinicRules.AllowedSlotIntervals)} minutes.");
    }
}
