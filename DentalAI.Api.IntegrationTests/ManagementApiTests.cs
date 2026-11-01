using System.Net;
using System.Net.Http.Json;
using DentalAI.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace DentalAI.Api.IntegrationTests;

public sealed record PatientRow(Guid Id, string FirstName, string LastName, string Phone, string? Email);
public sealed record PatientPage(List<PatientRow> Items, int TotalCount, int Page, int PageSize);
public sealed record ServiceRow(Guid Id, Guid ClinicId, string Name);

/// <summary>Management pages: patient search and paging, super-admin services, and doctor deletion rules.</summary>
[Collection(ApiCollection.Name)]
public sealed class ManagementApiTests(ApiFactory factory) : IAsyncLifetime
{
    private SchedulingApi _api = null!;

    public async Task InitializeAsync() => _api = await SchedulingApi.SignInAsync(factory);

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PatientSearch_MatchesEveryWordAndPages()
    {
        var tag = $"Zq{Guid.NewGuid():N}"[..10]; // a last name no other test uses
        for (var i = 0; i < 5; i++)
        {
            await CreatePatientAsync($"Anna{i}", tag, $"+1 555 020 01{i:00}");
        }
        await CreatePatientAsync("Bob", tag, "+1 555 020 0199");

        var firstPage = await SearchAsync(tag, page: 1, pageSize: 4);
        var secondPage = await SearchAsync(tag, page: 2, pageSize: 4);
        var twoWords = await SearchAsync($"bob {tag}");
        var byPhone = await SearchAsync("555 020 0199");

        Assert.Equal(6, firstPage.TotalCount);
        Assert.Equal(4, firstPage.Items.Count);
        Assert.Equal(2, secondPage.Items.Count);
        Assert.Empty(firstPage.Items.Select(x => x.Id).Intersect(secondPage.Items.Select(x => x.Id)));
        Assert.Equal("Bob", Assert.Single(twoWords.Items).FirstName);
        Assert.Contains(byPhone.Items, x => x.FirstName == "Bob" && x.LastName == tag);
    }

    [Fact]
    public async Task PatientSearch_ClampsThePageSize()
    {
        var page = await SearchAsync(null, page: 0, pageSize: 5000);

        Assert.Equal(1, page.Page);
        Assert.Equal(100, page.PageSize);
    }

    [Fact]
    public async Task SuperAdmin_CreatesAServiceInTheChosenClinic()
    {
        var superAdmin = await SchedulingApi.SignInAsync(factory, "superadmin@dentalai.local", "SuperAdmin123!");

        var withoutClinic = await superAdmin.Http.PostAsJsonAsync("/api/services",
            new { name = "Whitening", price = 200m, durationMinutes = 60, isActive = true }, SchedulingApi.Json);
        var created = await SchedulingApi.ReadAsync<ServiceRow>(await superAdmin.Http.PostAsJsonAsync("/api/services",
            new { clinicId = SchedulingApi.ClinicId, name = "Whitening", price = 200m, durationMinutes = 60, isActive = true },
            SchedulingApi.Json));

        Assert.Equal(HttpStatusCode.BadRequest, withoutClinic.StatusCode);
        Assert.Equal(SchedulingApi.ClinicId, created.ClinicId);
    }

    [Fact]
    public async Task ClinicAdmin_CannotPlaceAServiceInAnotherClinic()
    {
        var created = await SchedulingApi.ReadAsync<ServiceRow>(await _api.Http.PostAsJsonAsync("/api/services",
            new { clinicId = Guid.NewGuid(), name = "Sealant", price = 50m, durationMinutes = 30, isActive = true },
            SchedulingApi.Json));

        Assert.Equal(SchedulingApi.ClinicId, created.ClinicId);
    }

    [Fact]
    public async Task DeletingADoctor_WithUpcomingAppointments_Returns409_UntilTheyAreCancelled()
    {
        var doctorId = await _api.CreateDoctorAsync();
        var serviceId = await _api.CreateServiceAsync();
        var patientId = await _api.CreatePatientAsync();
        var slot = (await _api.GetSlotsAsync(doctorId, serviceId, SchedulingApi.FutureClinicDate()))[0];
        var appointment = await _api.BookOkAsync(doctorId, serviceId, patientId, slot.StartTimeUtc);

        var blocked = await _api.Http.DeleteAsync($"/api/doctors/{doctorId}");
        await _api.CancelOkAsync(appointment.Id);
        var allowed = await _api.Http.DeleteAsync($"/api/doctors/{doctorId}");

        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Contains("1 upcoming appointment", await blocked.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    private async Task CreatePatientAsync(string firstName, string lastName, string phone) =>
        await SchedulingApi.ReadAsync<PatientRow>(await _api.Http.PostAsJsonAsync("/api/patients",
            new { firstName, lastName, phone }, SchedulingApi.Json));

    private async Task<PatientPage> SearchAsync(string? search, int page = 1, int pageSize = 20) =>
        await SchedulingApi.ReadAsync<PatientPage>(await _api.Http.GetAsync(
            $"/api/patients/search?search={Uri.EscapeDataString(search ?? "")}&page={page}&pageSize={pageSize}"));
}
