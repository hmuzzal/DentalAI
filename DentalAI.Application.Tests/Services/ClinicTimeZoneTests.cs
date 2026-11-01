using DentalAI.Application.DTOs.Clinics;
using DentalAI.Application.Services;
using DentalAI.Application.Validators;
using Xunit;

namespace DentalAI.Application.Tests.Services;

public sealed class ClinicTimeZoneTests
{
    [Theory]
    [InlineData("America/New_York", 15, true)]
    [InlineData("Pacific/Honolulu", 30, true)]
    [InlineData("Asia/Dhaka", 10, true)]
    [InlineData("Mars/Olympus_Mons", 15, false)]
    [InlineData("", 15, false)]
    [InlineData("America/Chicago", 7, false)]
    [InlineData("America/Chicago", 0, false)]
    public void ClinicValidators_CheckTheZoneAndSlotInterval(string zone, int interval, bool valid)
    {
        var create = new CreateClinicDto
        {
            Name = "Clinic", Phone = "+1 212 555 0100", Email = "a@b.com", Address = "1 Main St",
            TimeZone = zone, SlotIntervalMinutes = interval
        };
        var update = new UpdateClinicDto
        {
            Name = "Clinic", Phone = "+1 212 555 0100", Email = "a@b.com", Address = "1 Main St",
            TimeZone = zone, SlotIntervalMinutes = interval
        };

        Assert.Equal(valid, new CreateClinicDtoValidator().Validate(create).IsValid);
        Assert.Equal(valid, new UpdateClinicDtoValidator().Validate(update).IsValid);
    }

    [Fact]
    public void Catalog_ListsUsZonesFirst_AndEveryIdResolves()
    {
        var zones = TimeZoneCatalog.List(new DateTime(2026, 7, 6, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal("America/New_York", zones[0].Id);
        Assert.Equal("(UTC-04:00) Eastern Time", zones[0].Label);
        Assert.Equal(8, zones.TakeWhile(x => x.Group == TimeZoneCatalog.UnitedStatesGroup).Count());
        Assert.Contains(zones, x => x.Id == "Asia/Dhaka" && x.UtcOffsetMinutes == 360);
        Assert.Contains(zones, x => x.Id == "UTC");
        Assert.Equal(zones.Count, zones.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(zones, x => Assert.True(SchedulingEngine.TryFindTimeZone(x.Id, out _), x.Id));
    }

    [Fact]
    public void Catalog_OffsetsFollowDaylightSaving()
    {
        var winter = TimeZoneCatalog.List(new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal(-300, winter.Single(x => x.Id == "America/New_York").UtcOffsetMinutes);
        Assert.Equal(-420, winter.Single(x => x.Id == "America/Phoenix").UtcOffsetMinutes);
    }
}
