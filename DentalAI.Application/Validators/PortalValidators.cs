using DentalAI.Application.DTOs.Portal;
using DentalAI.Domain.Enums;
using FluentValidation;

namespace DentalAI.Application.Validators;

/// <summary>Shape checks. Emails, phones and codes are checked by <c>PortalService</c>, which knows the clinic's country.</summary>
public sealed class PortalStartRequestValidator : AbstractValidator<PortalStartRequest>
{
    public PortalStartRequestValidator()
    {
        RuleFor(x => x.Contact).NotEmpty().WithMessage("Enter your email address or mobile number.").MaximumLength(200);
    }
}

public sealed class PortalVerifyRequestValidator : AbstractValidator<PortalVerifyRequest>
{
    public PortalVerifyRequestValidator()
    {
        RuleFor(x => x.Contact).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Code).NotEmpty().WithMessage("Enter the 6-digit code.")
            .Matches(@"^\s*\d{6}\s*$").WithMessage("The code has 6 digits.");
    }
}

public sealed class PortalSelectRequestValidator : AbstractValidator<PortalSelectRequest>
{
    public PortalSelectRequestValidator()
    {
        RuleFor(x => x.Ticket).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PatientId).NotEmpty();
    }
}

public sealed class PortalRegisterRequestValidator : AbstractValidator<PortalRegisterRequest>
{
    public PortalRegisterRequestValidator()
    {
        RuleFor(x => x.Ticket).NotEmpty().MaximumLength(200);
        RuleFor(x => x.FirstName).NotEmpty().WithMessage("Enter your first name.").MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().WithMessage("Enter your last name.").MaximumLength(100);
        RuleFor(x => x.DateOfBirth).NotNull().WithMessage("Enter your date of birth.")
            .LessThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))).WithMessage("Date of birth can't be in the future.")
            .GreaterThan(new DateOnly(1900, 1, 1)).WithMessage("Enter a valid date of birth.");
        RuleFor(x => x.Phone).MaximumLength(30);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(200).When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}

public sealed class PortalRefreshRequestValidator : AbstractValidator<PortalRefreshRequest>
{
    public PortalRefreshRequestValidator()
    {
        RuleFor(x => x.RefreshToken).MaximumLength(200);
    }
}

public sealed class PortalUpdateProfileRequestValidator : AbstractValidator<PortalUpdateProfileRequest>
{
    public PortalUpdateProfileRequestValidator()
    {
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(30)
            .Matches(@"^[0-9+\-\s().]{7,30}$").WithMessage("Enter a valid phone number.");
        RuleFor(x => x.Email).EmailAddress().MaximumLength(200).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.AddressLine1).MaximumLength(200);
        RuleFor(x => x.AddressLine2).MaximumLength(200);
        RuleFor(x => x.City).MaximumLength(100);
        RuleFor(x => x.State).MaximumLength(50);
        RuleFor(x => x.PostalCode).MaximumLength(20)
            .Matches(@"^[A-Za-z0-9][A-Za-z0-9\- ]{1,18}[A-Za-z0-9]$").WithMessage("Enter a valid ZIP or postal code.")
            .When(x => !string.IsNullOrWhiteSpace(x.PostalCode));
        RuleFor(x => x.PreferredContactMethod).IsInEnum();
        RuleFor(x => x.SmsOptIn).Equal(true)
            .When(x => x.PreferredContactMethod == ContactMethod.Sms)
            .WithMessage("To be contacted by text, agree to receive text messages.");
        RuleFor(x => x.Email).NotEmpty()
            .When(x => x.PreferredContactMethod == ContactMethod.Email)
            .WithMessage("Add an email address to be contacted by email.");
    }
}

public sealed class PortalBookRequestValidator : AbstractValidator<PortalBookRequest>
{
    public PortalBookRequestValidator()
    {
        RuleFor(x => x.DoctorId).NotEmpty();
        RuleFor(x => x.ServiceId).NotEmpty();
        RuleFor(x => x.StartTime).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(500);
    }
}

public sealed class PortalRescheduleRequestValidator : AbstractValidator<PortalRescheduleRequest>
{
    public PortalRescheduleRequestValidator()
    {
        RuleFor(x => x.StartTime).NotEmpty();
    }
}

public sealed class UpdatePortalSettingsRequestValidator : AbstractValidator<UpdatePortalSettingsRequest>
{
    public UpdatePortalSettingsRequestValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().WithMessage("Enter the portal's address.").MaximumLength(40);
        RuleFor(x => x.MinNoticeHours).InclusiveBetween(0, 168).WithMessage("Minimum notice must be 0–168 hours.");
        RuleFor(x => x.MaxDaysAhead).InclusiveBetween(1, 365).WithMessage("Patients can book 1–365 days ahead.");
        RuleFor(x => x.CancelCutoffHours).InclusiveBetween(0, 168).WithMessage("The change cutoff must be 0–168 hours.");
        RuleFor(x => x.MaxUpcomingAppointments).InclusiveBetween(1, 20).WithMessage("Allow 1–20 upcoming appointments per patient.");
    }
}
