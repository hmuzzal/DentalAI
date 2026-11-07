using DentalAI.Application.DTOs.Notifications;
using FluentValidation;

namespace DentalAI.Application.Validators;

public sealed class UpdateNotificationSettingsRequestValidator : AbstractValidator<UpdateNotificationSettingsRequest>
{
    /// <summary>The reminder job looks this far ahead (see ReminderScheduler).</summary>
    public const int MaxReminderHours = 72;

    public UpdateNotificationSettingsRequestValidator()
    {
        RuleFor(x => x.FirstReminderHours).InclusiveBetween(1, MaxReminderHours)
            .WithMessage($"The first reminder must be 1 to {MaxReminderHours} hours before the appointment.");
        RuleFor(x => x.SecondReminderHours).InclusiveBetween(0, MaxReminderHours)
            .WithMessage($"The second reminder must be 0 (off) to {MaxReminderHours} hours before the appointment.");
        RuleFor(x => x.SecondReminderHours).LessThan(x => x.FirstReminderHours)
            .When(x => x.SecondReminderHours > 0)
            .WithMessage("The second reminder must come closer to the appointment than the first.");
    }
}

public sealed class SendTestMessageRequestValidator : AbstractValidator<SendTestMessageRequest>
{
    public SendTestMessageRequestValidator()
    {
        RuleFor(x => x.Channel).IsInEnum();
        RuleFor(x => x.To).NotEmpty().MaximumLength(200);
    }
}
