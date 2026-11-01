using System.Net;
using System.Net.Http.Json;
using DentalAI.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace DentalAI.Api.IntegrationTests;

/// <summary>
/// Platform admins (SuperAdmin) run the platform but never see a clinic's patients, appointments, doctors or
/// services (HIPAA minimum necessary). They get clinics, lookups and a counts-only platform overview.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SuperAdminAccessTests(ApiFactory factory)
{
    private Task<SchedulingApi> PlatformAdminAsync() =>
        SchedulingApi.SignInAsync(factory, "superadmin@dentalai.local", "SuperAdmin123!");

    [Theory]
    [InlineData("/api/doctors")]
    [InlineData("/api/patients")]
    [InlineData("/api/patients/search")]
    [InlineData("/api/services")]
    [InlineData("/api/appointments")]
    [InlineData("/api/calendar/week?date=2027-06-07")]
    [InlineData("/api/dashboard/stats")]
    [InlineData("/api/dashboard/scheduling")]
    public async Task PlatformAdmin_CannotReadClinicData(string url)
    {
        var response = await (await PlatformAdminAsync()).Http.GetAsync(url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/doctors")]
    [InlineData("/api/patients")]
    [InlineData("/api/services")]
    public async Task PlatformAdmin_CannotCreateClinicData(string url)
    {
        var response = await (await PlatformAdminAsync()).Http.PostAsJsonAsync(url, new
        {
            clinicId = SchedulingApi.ClinicId, firstName = "X", lastName = "Y", name = "Z", phone = "+1 212 555 0100",
            specialty = "General", email = "x@example.com", price = 1m, durationMinutes = 30, isActive = true
        }, SchedulingApi.Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/clinics")]
    [InlineData("/api/platform/overview")]
    [InlineData("/api/lookups/time-zones")]
    [InlineData("/api/lookups/countries")]
    [InlineData("/api/auth/me")]
    public async Task PlatformAdmin_CanUsePlatformEndpoints(string url)
    {
        var response = await (await PlatformAdminAsync()).Http.GetAsync(url);

        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"{url} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task PlatformOverview_CountsPatientsButNeverShowsThem()
    {
        var clinicAdmin = await SchedulingApi.SignInAsync(factory);
        var name = $"Zx{Guid.NewGuid():N}"[..12];
        await SchedulingApi.ReadAsync<IdDto>(await clinicAdmin.Http.PostAsJsonAsync("/api/patients",
            new { firstName = name, lastName = name, phone = "+1 212 555 0177", email = $"{name}@example.com" }, SchedulingApi.Json));

        var response = await (await PlatformAdminAsync()).Http.GetAsync("/api/platform/overview");
        var body = await response.Content.ReadAsStringAsync();
        var overview = await SchedulingApi.ReadAsync<PlatformOverview>(response);

        Assert.DoesNotContain(name, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("2125550177", body);
        var demo = Assert.Single(overview.ClinicUsage, c => c.ClinicId == SchedulingApi.ClinicId);
        Assert.True(demo.Patients >= 1);
        Assert.Equal(overview.ClinicUsage.Sum(c => c.Patients), overview.Patients);
    }

    [Fact]
    public async Task ClinicStaff_CannotSeeThePlatformOverview()
    {
        var response = await (await SchedulingApi.SignInAsync(factory)).Http.GetAsync("/api/platform/overview");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed record ClinicUsage(Guid ClinicId, string Name, int Patients);
    private sealed record PlatformOverview(int Clinics, int Patients, List<ClinicUsage> ClinicUsage);
}
