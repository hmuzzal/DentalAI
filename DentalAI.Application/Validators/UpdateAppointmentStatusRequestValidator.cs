using DentalAI.Application.DTOs.Appointments;
using FluentValidation;

namespace DentalAI.Application.Validators;

public sealed class UpdateAppointmentStatusRequestValidator : AbstractValidator<UpdateAppointmentStatusRequest>
{
    public UpdateAppointmentStatusRequestValidator()
    {
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}
