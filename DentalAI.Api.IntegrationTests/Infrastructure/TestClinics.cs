using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace DentalAI.Api.IntegrationTests.Infrastructure;

/// <summary>A fresh clinic with a signed-in admin and receptionist, for tests that change clinic-wide settings.</summary>
public sealed record TestClinic(Guid Id, SchedulingApi Admin, SchedulingApi Receptionist);

public static class TestClinics
{
    private const string Password = "Welcome2026!";

    public static async Task<TestClinic> CreateAsync(ApiFactory factory, string country = "US", string timeZone = "America/New_York")
    {
        var platform = await SchedulingApi.SignInAsync(factory, "superadmin@dentalai.local", "SuperAdmin123!");
        var clinicId = (await SchedulingApi.ReadAsync<IdDto>(await platform.Http.PostAsJsonAsync("/api/clinics", new
        {
            name = $"Clinic {Guid.NewGuid():N}"[..20],
            phone = country == "BD" ? "+880 1711-000101" : "(212) 555-0100",
            email = "clinic@test.local",
            address = "1 Main St",
            timeZone,
            country,
            slotIntervalMinutes = 15
        }, SchedulingApi.Json))).Id;

        var adminEmail = await InviteAndAcceptAsync(factory, platform, $"/api/clinics/{clinicId}/staff/invite", "ClinicAdmin");
        var admin = await SchedulingApi.SignInAsync(factory, adminEmail, Password);
        var receptionistEmail = await InviteAndAcceptAsync(factory, admin, "/api/staff/invite", "Receptionist");
        var receptionist = await SchedulingApi.SignInAsync(factory, receptionistEmail, Password);
        return new TestClinic(clinicId, admin, receptionist);
    }

    private static async Task<string> InviteAndAcceptAsync(ApiFactory factory, SchedulingApi inviter, string url, string role)
    {
        var email = $"staff.{Guid.NewGuid():N}@test.local";
        var invite = await SchedulingApi.ReadAsync<StaffInvite>(await inviter.Http.PostAsJsonAsync(url,
            new { firstName = "Test", lastName = role, email, role }, SchedulingApi.Json));
        var accepted = await factory.CreateClient().PostAsJsonAsync("/api/auth/accept-invite",
            new { token = invite.InviteToken, password = Password });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        return email;
    }
}
