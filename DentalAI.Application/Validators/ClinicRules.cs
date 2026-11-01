using DentalAI.Application.Services;

namespace DentalAI.Application.Validators;

/// <summary>Rules shared by the create and update clinic validators.</summary>
public static class ClinicRules
{
    public static readonly IReadOnlyList<int> AllowedSlotIntervals = [5, 10, 15, 20, 30, 60];

    public static bool IsKnownTimeZone(string? id) => SchedulingEngine.TryFindTimeZone(id, out _);

    public static bool IsAllowedSlotInterval(int minutes) => AllowedSlotIntervals.Contains(minutes);
}
