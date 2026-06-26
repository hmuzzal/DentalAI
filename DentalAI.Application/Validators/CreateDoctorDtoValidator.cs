using DentalAI.Application.DTOs.Doctors;
using FluentValidation;

namespace DentalAI.Application.Validators;

public sealed class CreateDoctorDtoValidator : AbstractValidator<CreateDoctorDto>
{
    private const string PhonePattern = @"^\+?[0-9\s\-().]{7,20}$";

    public CreateDoctorDtoValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Specialty).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Phone).NotEmpty().Matches(PhonePattern);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
    }
}
