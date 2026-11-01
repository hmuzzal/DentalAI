using System.Net;
using System.Net.Http.Json;
using DentalAI.Api.IntegrationTests.Infrastructure;
using DentalAI.Domain.Entities;
using DentalAI.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DentalAI.Api.IntegrationTests;

public sealed record TimeZoneOptionDto(string Id, string Label, int UtcOffsetMinutes, string Group);
public sealed record ClinicDto(Guid Id, string Name, string TimeZone, int SlotIntervalMinutes);

/// <summary>Clinic time zones and slot intervals end to end: lookups, validation, and slots across US DST changes.</summary>
[Collection(ApiCollection.Name)]
public sealed class ClinicTimeZoneApiTests(ApiFactory factory) : IAsyncLifetime
{
    private const string NewYork = "America/New_York";

    // The next US transitions after these tests were written (2026-11-01): spring forward 14 March 2027,
    // fall back 7 November 2027, both at 02:00 local time.
    private static readonly DateOnly SpringForward = new(2027, 3, 14);
    private static readonly DateOnly FallBack = new(2027, 11, 7);

    private SchedulingApi _superAdmin = null!;
    private SchedulingApi _clinicAdmin = null!;

    public async Task InitializeAsync()
    {
        _superAdmin = await SchedulingApi.SignInAsync(factory, "superadmin@dentalai.local", "SuperAdmin123!");
        _clinicAdmin = await SchedulingApi.SignInAsync(factory);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task TimeZoneLookup_ListsUsZonesFirst()
    {
        var zones = await SchedulingApi.ReadAsync<List<TimeZoneOptionDto>>(
            await _clinicAdmin.Http.GetAsync("/api/lookups/time-zones"));

        Assert.Equal(NewYork, zones[0].Id);
        Assert.Equal("United States", zones[0].Group);
        Assert.Contains(zones, x => x.Id == SchedulingApi.ClinicTimeZone);
    }

    [Theory]
    [InlineData("Mars/Olympus_Mons", 15)]
    [InlineData(NewYork, 7)]
    public async Task CreateClinic_WithAnUnknownZoneOrInterval_Returns400(string zone, int interval)
    {
        var response = await _superAdmin.Http.PostAsJsonAsync("/api/clinics", ClinicRequest(zone, interval), SchedulingApi.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateClinic_SavesTheZoneAndInterval()
    {
        var clinic = await SchedulingApi.ReadAsync<ClinicDto>(
            await _superAdmin.Http.PostAsJsonAsync("/api/clinics", ClinicRequest(NewYork, 30), SchedulingApi.Json));

        Assert.Equal(NewYork, clinic.TimeZone);
        Assert.Equal(30, clinic.SlotIntervalMinutes);
    }

    [Fact]
    public async Task Slots_UseTheClinicIntervalAndCarryTheClinicZone()
    {
        var setup = await SeedNewYorkClinicAsync(intervalMinutes: 20, new TimeOnly(9, 0), new TimeOnly(10, 0));
        var date = new DateOnly(2027, 6, 7);

        var slots = await _superAdmin.GetSlotsAsync(setup.DoctorId, setup.ServiceId, date);

        // 09:00 EDT = 13:00Z; a 30-minute service on a 20-minute grid fits at 09:00 and 09:20.
        Assert.Equal([Utc(2027, 6, 7, 13, 0), Utc(2027, 6, 7, 13, 20)], slots.Select(x => x.StartTimeUtc));
        Assert.All(slots, x => Assert.Equal(NewYork, x.ClinicTimeZone));
    }

    [Fact]
    public async Task SpringForwardDay_SkipsTheMissingHour_AndTheSlotAfterItBooks()
    {
        var setup = await SeedNewYorkClinicAsync(intervalMinutes: 30, new TimeOnly(1, 0), new TimeOnly(4, 0));

        var slots = await _superAdmin.GetSlotsAsync(setup.DoctorId, setup.ServiceId, SpringForward);

        // 01:00 and 01:30 EST, then 03:00 and 03:30 EDT. 02:00–02:59 does not exist that day.
        Assert.Equal(
            [Utc(2027, 3, 14, 6, 0), Utc(2027, 3, 14, 6, 30), Utc(2027, 3, 14, 7, 0), Utc(2027, 3, 14, 7, 30)],
            slots.Select(x => x.StartTimeUtc));

        var booked = await _superAdmin.BookAsync(setup.DoctorId, setup.ServiceId, setup.PatientId, Utc(2027, 3, 14, 7, 0));
        Assert.Equal(HttpStatusCode.OK, booked.StatusCode);
    }

    [Fact]
    public async Task FallBackDay_OffersTheRepeatedHourOnce_AndBothOccurrencesBlockEachOther()
    {
        var setup = await SeedNewYorkClinicAsync(intervalMinutes: 30, new TimeOnly(0, 0), new TimeOnly(3, 0));

        var slots = await _superAdmin.GetSlotsAsync(setup.DoctorId, setup.ServiceId, FallBack);

        // 00:00, 00:30 EDT; 01:00, 01:30 first occurrence (EDT); then 02:00, 02:30 EST. 06:00Z–07:00Z is the
        // repeated 01:xx (EST) hour, which is never offered.
        Assert.Equal(
            [Utc(2027, 11, 7, 4, 0), Utc(2027, 11, 7, 4, 30), Utc(2027, 11, 7, 5, 0), Utc(2027, 11, 7, 5, 30),
             Utc(2027, 11, 7, 7, 0), Utc(2027, 11, 7, 7, 30)],
            slots.Select(x => x.StartTimeUtc));

        Assert.Equal(HttpStatusCode.OK,
            (await _superAdmin.BookAsync(setup.DoctorId, setup.ServiceId, setup.PatientId, Utc(2027, 11, 7, 5, 30))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await _superAdmin.BookAsync(setup.DoctorId, setup.ServiceId, setup.PatientId, Utc(2027, 11, 7, 5, 45))).StatusCode);
    }

    private sealed record Setup(Guid DoctorId, Guid ServiceId, Guid PatientId);

    /// <summary>A New York clinic with one doctor (same hours every day), a 30-minute service and a patient.</summary>
    private async Task<Setup> SeedNewYorkClinicAsync(int intervalMinutes, TimeOnly from, TimeOnly to)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var clinic = new Clinic
        {
            Name = $"NY Clinic {Guid.NewGuid():N}"[..24], Phone = "+1 212 555 0100", Email = "ny@test.local",
            Address = "1 Broadway, New York, NY", TimeZone = NewYork, SlotIntervalMinutes = intervalMinutes
        };
        var doctor = new Doctor
        {
            ClinicId = clinic.Id, FirstName = "Test", LastName = "Doctor", Specialty = "General",
            Phone = "+1 212 555 0101", Email = $"doctor.{Guid.NewGuid():N}@test.local"
        };
        var service = new Service { ClinicId = clinic.Id, Name = "Cleaning", Price = 80m, DurationMinutes = 30, IsActive = true };
        var patient = new Patient { ClinicId = clinic.Id, FirstName = "Test", LastName = "Patient", Phone = "+1 212 555 0102" };

        db.Add(clinic);
        db.AddRange(doctor, service, patient);
        db.AddRange(Enum.GetValues<DayOfWeek>().Select(day => new DoctorSchedule
        {
            ClinicId = clinic.Id, DoctorId = doctor.Id, DayOfWeek = day, StartTime = from, EndTime = to, IsWorking = true
        }));
        await db.SaveChangesAsync();

        return new Setup(doctor.Id, service.Id, patient.Id);
    }

    private static object ClinicRequest(string zone, int interval) => new
    {
        name = $"Clinic {Guid.NewGuid():N}"[..20],
        phone = "+1 212 555 0100",
        email = "clinic@test.local",
        address = "1 Main St, New York, NY",
        timeZone = zone,
        slotIntervalMinutes = interval
    };

    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);
}
