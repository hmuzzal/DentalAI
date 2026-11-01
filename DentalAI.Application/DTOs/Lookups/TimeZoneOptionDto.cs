namespace DentalAI.Application.DTOs.Lookups;

/// <param name="Id">IANA time-zone ID, e.g. <c>America/New_York</c>.</param>
/// <param name="Label">Display text with the current UTC offset, e.g. "(UTC-05:00) Eastern Time".</param>
/// <param name="UtcOffsetMinutes">The zone's offset right now (it changes with daylight saving).</param>
/// <param name="Group">"United States" or "Other".</param>
public sealed record TimeZoneOptionDto(string Id, string Label, int UtcOffsetMinutes, string Group);
