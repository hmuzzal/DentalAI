using DentalAI.Application.DTOs.Lookups;

namespace DentalAI.Application.Services;

/// <summary>The time zones a clinic can pick: the main US zones first, then every other zone by offset.</summary>
public static class TimeZoneCatalog
{
    public const string UnitedStatesGroup = "United States";
    public const string OtherGroup = "Other";

    private static readonly (string Id, string Name)[] UnitedStates =
    [
        ("America/New_York", "Eastern Time"),
        ("America/Chicago", "Central Time"),
        ("America/Denver", "Mountain Time"),
        ("America/Phoenix", "Mountain Time - Arizona (no DST)"),
        ("America/Los_Angeles", "Pacific Time"),
        ("America/Anchorage", "Alaska Time"),
        ("Pacific/Honolulu", "Hawaii Time"),
        ("America/Puerto_Rico", "Atlantic Time - Puerto Rico")
    ];

    public static IReadOnlyList<TimeZoneOptionDto> List(DateTime utcNow)
    {
        utcNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        var result = new List<TimeZoneOptionDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (id, name) in UnitedStates)
        {
            if (SchedulingEngine.TryFindTimeZone(id, out var zone) && seen.Add(id))
            {
                result.Add(Option(id, name, zone, utcNow, UnitedStatesGroup));
            }
        }

        var others = new List<TimeZoneOptionDto>();
        foreach (var zone in TimeZoneInfo.GetSystemTimeZones())
        {
            var ianaId = zone.HasIanaId ? zone.Id
                : TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var converted) ? converted : null;
            if (ianaId is null || ianaId.StartsWith("Etc/", StringComparison.Ordinal) || !seen.Add(ianaId))
            {
                continue;
            }

            others.Add(Option(ianaId, ianaId.Replace('_', ' '), zone, utcNow, OtherGroup));
        }

        if (seen.Add("UTC"))
        {
            others.Add(Option("UTC", "Coordinated Universal Time", TimeZoneInfo.Utc, utcNow, OtherGroup));
        }

        result.AddRange(others.OrderBy(x => x.UtcOffsetMinutes).ThenBy(x => x.Id, StringComparer.Ordinal));
        return result;
    }

    private static TimeZoneOptionDto Option(string id, string name, TimeZoneInfo zone, DateTime utcNow, string group)
    {
        var offset = zone.GetUtcOffset(utcNow);
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var label = $"(UTC{sign}{offset.Duration():hh\\:mm}) {name}";
        return new TimeZoneOptionDto(id, label, (int)offset.TotalMinutes, group);
    }
}
