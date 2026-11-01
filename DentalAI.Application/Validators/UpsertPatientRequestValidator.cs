using DentalAI.Application.DTOs.Patients;
using DentalAI.Domain.Enums;
using FluentValidation;

namespace DentalAI.Application.Validators;

/// <summary>
/// Shape checks. Whether the phone number is real is checked by the service, which knows the clinic's country.
/// </summary>
public sealed class UpsertPatientRequestValidator : AbstractValidator<UpsertPatientRequest>
{
    public UpsertPatientRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(30)
            .Matches(@"^[0-9+\-\s().]{7,30}$").WithMessage("Enter a valid phone number.");
        RuleFor(x => x.Email).EmailAddress().MaximumLength(200).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.DateOfBirth).LessThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))
            .WithMessage("Date of birth can't be in the future.");
        RuleFor(x => x.Gender).MaximumLength(30);
        RuleFor(x => x.Notes).MaximumLength(2000);

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
            .WithMessage("Text messages need the patient's SMS consent.");
        RuleFor(x => x.Email).NotEmpty()
            .When(x => x.PreferredContactMethod == ContactMethod.Email)
            .WithMessage("Add an email address to use email as the preferred contact method.");

        When(x => x.Insurances is not null, () =>
        {
            RuleFor(x => x.Insurances!.Count).LessThanOrEqualTo(2).WithMessage("A patient can have a primary and a secondary insurance.");
            RuleFor(x => x.Insurances!).Must(list => list.Select(i => i.Priority).Distinct().Count() == list.Count)
                .WithMessage("Each insurance must have a different priority (primary or secondary).");
            RuleFor(x => x.Insurances!).Must(list => list.All(i => i.Priority != InsurancePriority.Secondary)
                    || list.Any(i => i.Priority == InsurancePriority.Primary))
                .WithMessage("Add the primary insurance before a secondary one.");
            RuleForEach(x => x.Insurances!).ChildRules(policy =>
            {
                policy.RuleFor(i => i.Priority).IsInEnum();
                policy.RuleFor(i => i.CarrierName).NotEmpty().WithMessage("Enter the insurance carrier.").MaximumLength(150);
                policy.RuleFor(i => i.MemberId).NotEmpty().WithMessage("Enter the member ID.").MaximumLength(50);
                policy.RuleFor(i => i.GroupNumber).MaximumLength(50);
                policy.RuleFor(i => i.RelationshipToPatient).IsInEnum();
                policy.RuleFor(i => i.SubscriberName).NotEmpty()
                    .When(i => i.RelationshipToPatient != InsuranceRelationship.Self)
                    .WithMessage("Enter the subscriber's name.");
                policy.RuleFor(i => i.SubscriberName).MaximumLength(150);
                policy.RuleFor(i => i.SubscriberDateOfBirth)
                    .LessThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))
                    .WithMessage("The subscriber's date of birth can't be in the future.");
            });
        });
    }
}
