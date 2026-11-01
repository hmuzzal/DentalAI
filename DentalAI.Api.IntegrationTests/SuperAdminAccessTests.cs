using System.Net;
using DentalAI.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace DentalAI.Api.IntegrationTests;

/// <summary>
/// A super admin has no clinic. The tenant query filter once read <c>CurrentClinicId.Value</c>, so every query on a
/// clinic-scoped table threw for super admins (500). These endpoints must all work for them.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SuperAdminAccessTests(ApiFactory factory)
{
    [Theory]
    [InlineData("/api/doctors")]
    [InlineData("/api/patients")]
    [InlineData("/api/services")]
    [InlineData("/api/appointments")]
    [InlineData("/api/clinics")]
    [InlineData("/api/calendar/week?date=2027-06-07")]
    [InlineData("/api/dashboard/stats")]
    [InlineData("/api/dashboard/scheduling")]
    public async Task SuperAdmin_CanReadClinicScopedData(string url)
    {
        var superAdmin = await SchedulingApi.SignInAsync(factory, "superadmin@dentalai.local", "SuperAdmin123!");

        var response = await superAdmin.Http.GetAsync(url);

        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"{url} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }
}
