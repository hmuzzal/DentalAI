using DentalAI.Application.DTOs.Appointments;
using FluentValidation;

namespace DentalAI.Application.Validators;

public sealed class RescheduleAppointmentRequestValidator : AbstractValidator<RescheduleAppointmentRequest>
{
    public RescheduleAppointmentRequestValidator()
    {
        RuleFor(x => x.StartTime).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}
