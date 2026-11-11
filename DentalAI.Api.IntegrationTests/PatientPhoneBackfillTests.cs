using DentalAI.Api.IntegrationTests.Infrastructure;
using DentalAI.Domain.Entities;
using DentalAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DentalAI.Api.IntegrationTests;

/// <summary>
/// Phones saved before numbers were stored in E.164 are converted at startup, read in each clinic's country,
/// so portal sign-in by phone and texts work for those patients. Unreadable numbers are left for staff to fix.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PatientPhoneBackfillTests(ApiFactory factory)
{
    [Fact]
    public async Task OldFormatPhones_AreConvertedInTheClinicsCountry_AndUnreadableOnesAreLeft()
    {
        var us = NewClinic("US");
        var bd = NewClinic("BD");
        var usDashed = NewPatient(us, "(212) 555-0142");
        var usSpaced = NewPatient(us, "+1 212 555 0143");
        var bdLocal = NewPatient(bd, "01711-000102");
        var unreadable = NewPatient(us, "+1-555-0101"); // 555-0101 has no area code
        var alreadyE164 = NewPatient(us, "+12125550144");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Clinics.AddRange(us, bd);
            db.Patients.AddRange(usDashed, usSpaced, bdLocal, unreadable, alreadyE164);
            await db.SaveChangesAsync();

            var (converted, left) = await PatientPhoneBackfill.RunAsync(db, NullLogger.Instance, CancellationToken.None);
            Assert.True(converted >= 3);
            Assert.True(left >= 1);

            // Running again finds nothing new to convert.
            var (again, _) = await PatientPhoneBackfill.RunAsync(db, NullLogger.Instance, CancellationToken.None);
            Assert.Equal(0, again);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var phones = await db.Patients.IgnoreQueryFilters()
                .Where(p => p.ClinicId == us.Id || p.ClinicId == bd.Id)
                .ToDictionaryAsync(p => p.Id, p => p.Phone);

            Assert.Equal("+12125550142", phones[usDashed.Id]);
            Assert.Equal("+12125550143", phones[usSpaced.Id]);
            Assert.Equal("+8801711000102", phones[bdLocal.Id]);
            Assert.Equal("+1-555-0101", phones[unreadable.Id]);
            Assert.Equal("+12125550144", phones[alreadyE164.Id]);
        }
    }

    private static Clinic NewClinic(string country) => new()
    {
        Name = $"Backfill {country} {Guid.NewGuid():N}",
        Phone = "+12125550100",
        Email = $"{Guid.NewGuid():N}@test.local",
        Address = "1 Main St",
        TimeZone = country == "US" ? "America/New_York" : "Asia/Dhaka",
        Country = country
    };

    private static Patient NewPatient(Clinic clinic, string phone) => new()
    {
        ClinicId = clinic.Id,
        FirstName = "Old",
        LastName = "Record",
        Phone = phone
    };
}
