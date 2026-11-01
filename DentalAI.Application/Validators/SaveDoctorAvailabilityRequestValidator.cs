using DentalAI.Application.DTOs.Scheduling;
using FluentValidation;

namespace DentalAI.Application.Validators;

public sealed class SaveDoctorAvailabilityRequestValidator : AbstractValidator<SaveDoctorAvailabilityRequest>
{
    public SaveDoctorAvailabilityRequestValidator()
    {
        RuleForEach(x => x.Schedules).ChildRules(period =>
        {
            period.RuleFor(x => x.EndTime).GreaterThan(x => x.StartTime)
                .WithMessage("Schedule end time must be later than start time.");
        });
        RuleForEach(x => x.Breaks).ChildRules(period =>
        {
            period.RuleFor(x => x.EndTime).GreaterThan(x => x.StartTime)
                .WithMessage("Break end time must be later than start time.");
        });
        RuleForEach(x => x.Leaves).ChildRules(leave =>
        {
            leave.RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate);
            leave.RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        });
        RuleFor(x => x).Custom((request, context) =>
        {
            foreach (var day in Enum.GetValues<DayOfWeek>())
            {
                var schedules = request.Schedules.Where(x => x.DayOfWeek == day && x.IsWorking).OrderBy(x => x.StartTime).ToArray();
                if (schedules.Zip(schedules.Skip(1)).Any(x => x.First.EndTime > x.Second.StartTime))
                    context.AddFailure(nameof(request.Schedules), $"Working periods overlap on {day}.");

                foreach (var breakPeriod in request.Breaks.Where(x => x.DayOfWeek == day))
                    if (!schedules.Any(x => breakPeriod.StartTime >= x.StartTime && breakPeriod.EndTime <= x.EndTime))
                        context.AddFailure(nameof(request.Breaks), $"Break {breakPeriod.StartTime}-{breakPeriod.EndTime} is outside working hours on {day}.");
            }
        });
    }
}
