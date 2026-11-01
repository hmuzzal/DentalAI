using System.Net;
using System.Net.Http.Json;
using DentalAI.Api.IntegrationTests.Infrastructure;
using DentalAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DentalAI.Api.IntegrationTests;

public sealed record StaffMember(Guid Id, string FirstName, string Email, string Role, string Status, DateTime? InviteExpiresAtUtc);
public sealed record StaffInvite(StaffMember Member, string InviteToken, DateTime InviteExpiresAtUtc);
public sealed record InviteDetails(string FirstName, string Email, string ClinicName, string Role);
public sealed record SessionDto(string AccessToken, string RefreshToken);

/// <summary>
/// How people join: platform admins create a clinic and invite its first admin; clinic admins invite their staff.
/// Each invite is a single-use link; the invited person sets their own password. There is no self sign-up.
/// Rule tests that deactivate or demote admins use a fresh clinic, never the seeded one other tests sign in with.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class StaffInviteTests(ApiFactory factory)
{
    private const string GoodPassword = "Welcome2026!";

    [Fact]
    public async Task SelfSignUp_IsGone()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { clinicName = "X", firstName = "A", lastName = "B", adminEmail = "a@b.com", password = "Password123" });

        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task ClinicAdmin_InvitesAReceptionist_WhoSetsTheirOwnPasswordAndSignsIn()
    {
        var admin = await SchedulingApi.SignInAsync(factory);
        var email = NewEmail();

        var invite = await InviteAsync(admin, "/api/staff/invite", email, "Receptionist");
        var details = await SchedulingApi.ReadAsync<InviteDetails>(await Anonymous().GetAsync($"/api/auth/invite?token={invite.InviteToken}"));
        var beforeAccepting = await LoginAsync(email, GoodPassword);
        var weak = await AcceptAsync(invite.InviteToken, "short");
        var accepted = await AcceptAsync(invite.InviteToken, GoodPassword);
        var reused = await AcceptAsync(invite.InviteToken, "Another2026!");
        var signedIn = await LoginAsync(email, GoodPassword);

        Assert.Equal("Invited", invite.Member.Status);
        Assert.Equal("Demo Dental Clinic", details.ClinicName);
        Assert.Equal(HttpStatusCode.Unauthorized, beforeAccepting.StatusCode); // no password until the invite is used
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, reused.StatusCode);              // single use
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);

        var staff = await SchedulingApi.ReadAsync<List<StaffMember>>(await admin.Http.GetAsync("/api/staff"));
        Assert.Equal("Active", staff.Single(s => s.Email == email).Status);
    }

    [Fact]
    public async Task InviteLinks_ExpireAndAreReplacedWhenRenewed()
    {
        var admin = await SchedulingApi.SignInAsync(factory);
        var first = await InviteAsync(admin, "/api/staff/invite", NewEmail(), "Receptionist");

        var renewed = await SchedulingApi.ReadAsync<StaffInvite>(
            await admin.Http.PostAsync($"/api/staff/{first.Member.Id}/renew-invite", null));
        Assert.Equal(HttpStatusCode.NotFound, (await Anonymous().GetAsync($"/api/auth/invite?token={first.InviteToken}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Anonymous().GetAsync($"/api/auth/invite?token={renewed.InviteToken}")).StatusCode);

        await ExpireInviteAsync(first.Member.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await AcceptAsync(renewed.InviteToken, GoodPassword)).StatusCode);
        var staff = await SchedulingApi.ReadAsync<List<StaffMember>>(await admin.Http.GetAsync("/api/staff"));
        Assert.Equal("InviteExpired", staff.Single(s => s.Id == first.Member.Id).Status);
    }

    [Fact]
    public async Task Invites_RejectAnEmailThatAlreadyHasAnAccount()
    {
        var admin = await SchedulingApi.SignInAsync(factory);

        var response = await admin.Http.PostAsJsonAsync("/api/staff/invite",
            new { firstName = "Dup", lastName = "User", email = "clinicadmin@dentalai.local", role = "Receptionist" }, SchedulingApi.Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Receptionists_CantManageStaff()
    {
        var admin = await SchedulingApi.SignInAsync(factory);
        var email = NewEmail();
        var invite = await InviteAsync(admin, "/api/staff/invite", email, "Receptionist");
        await AcceptAsync(invite.InviteToken, GoodPassword);
        var receptionist = await SchedulingApi.SignInAsync(factory, email, GoodPassword);

        Assert.Equal(HttpStatusCode.Forbidden, (await receptionist.Http.GetAsync("/api/staff")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await receptionist.Http.GetAsync("/api/patients")).StatusCode);
    }

    [Fact]
    public async Task AdminRules_NoSelfChanges_AndTheLastAdminStays()
    {
        var (_, first) = await NewClinicWithAdminAsync();
        var me = (await SchedulingApi.ReadAsync<List<StaffMember>>(await first.Http.GetAsync("/api/staff"))).Single();

        // Alone: can't deactivate or demote yourself.
        Assert.Equal(HttpStatusCode.Conflict, (await first.Http.PostAsync($"/api/staff/{me.Id}/deactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await first.Http.PutAsJsonAsync($"/api/staff/{me.Id}/role", new { role = "Receptionist" }, SchedulingApi.Json)).StatusCode);

        // A second admin who hasn't accepted yet doesn't count as an active admin...
        var secondEmail = NewEmail();
        var second = await InviteAsync(first, "/api/staff/invite", secondEmail, "ClinicAdmin");
        await AcceptAsync(second.InviteToken, GoodPassword);
        var secondApi = await SchedulingApi.SignInAsync(factory, secondEmail, GoodPassword);

        // ...but once active, either admin can demote the other while one admin remains.
        Assert.Equal(HttpStatusCode.OK, (await secondApi.Http.PutAsJsonAsync($"/api/staff/{me.Id}/role", new { role = "Receptionist" }, SchedulingApi.Json)).StatusCode);
        // Now the second is the only admin: nobody can remove them.
        var promoted = await SchedulingApi.SignInAsync(factory, secondEmail, GoodPassword);
        var secondId = (await SchedulingApi.ReadAsync<List<StaffMember>>(await promoted.Http.GetAsync("/api/staff")))
            .Single(s => s.Email == secondEmail).Id;
        Assert.Equal(HttpStatusCode.Conflict, (await promoted.Http.PostAsync($"/api/staff/{secondId}/deactivate", null)).StatusCode);
    }

    [Fact]
    public async Task ChangingARole_EndsThatPersonsSessions()
    {
        var (_, admin) = await NewClinicWithAdminAsync();
        var email = NewEmail();
        var invite = await InviteAsync(admin, "/api/staff/invite", email, "Receptionist");
        var session = await SchedulingApi.ReadAsync<SessionDto>(await AcceptAsync(invite.InviteToken, GoodPassword));

        await admin.Http.PutAsJsonAsync($"/api/staff/{invite.Member.Id}/role", new { role = "ClinicAdmin" }, SchedulingApi.Json);

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(session.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email, GoodPassword)).StatusCode);
    }

    [Fact]
    public async Task DeactivatedStaff_CantSignInOrRefresh_UntilReactivated()
    {
        var (_, admin) = await NewClinicWithAdminAsync();
        var email = NewEmail();
        var invite = await InviteAsync(admin, "/api/staff/invite", email, "Receptionist");
        var session = await SchedulingApi.ReadAsync<SessionDto>(await AcceptAsync(invite.InviteToken, GoodPassword));

        await admin.Http.PostAsync($"/api/staff/{invite.Member.Id}/deactivate", null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(email, GoodPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(session.RefreshToken)).StatusCode);

        await admin.Http.PostAsync($"/api/staff/{invite.Member.Id}/reactivate", null);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email, GoodPassword)).StatusCode);
    }

    [Fact]
    public async Task PlatformAdmin_OnboardsAClinic_ButOnlyInvitesAdmins()
    {
        var platform = await PlatformAdminAsync();
        var clinic = await CreateClinicAsync(platform);

        var receptionist = await platform.Http.PostAsJsonAsync($"/api/clinics/{clinic}/staff/invite",
            new { firstName = "R", lastName = "R", email = NewEmail(), role = "Receptionist" }, SchedulingApi.Json);
        var adminInvite = await InviteAsync(platform, $"/api/clinics/{clinic}/staff/invite", NewEmail(), "ClinicAdmin");
        var staff = await SchedulingApi.ReadAsync<List<StaffMember>>(await platform.Http.GetAsync($"/api/clinics/{clinic}/staff"));

        Assert.Equal(HttpStatusCode.Forbidden, receptionist.StatusCode);
        Assert.Equal("ClinicAdmin", Assert.Single(staff).Role);
        Assert.Equal(HttpStatusCode.OK, (await AcceptAsync(adminInvite.InviteToken, GoodPassword)).StatusCode);
    }

    [Fact]
    public async Task DeletingAClinic_LocksOutItsStaff()
    {
        var (clinic, admin) = await NewClinicWithAdminAsync();
        var adminEmail = (await SchedulingApi.ReadAsync<List<StaffMember>>(await admin.Http.GetAsync("/api/staff"))).Single().Email;
        var pending = await InviteAsync(admin, "/api/staff/invite", NewEmail(), "Receptionist");
        var session = await SchedulingApi.ReadAsync<SessionDto>(await LoginAsync(adminEmail, GoodPassword));

        Assert.Equal(HttpStatusCode.OK, (await (await PlatformAdminAsync()).Http.DeleteAsync($"/api/clinics/{clinic}")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(adminEmail, GoodPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(session.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.Http.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AcceptAsync(pending.InviteToken, GoodPassword)).StatusCode);
    }

    // ---- helpers ----

    private static string NewEmail() => $"staff.{Guid.NewGuid():N}@test.local";

    private HttpClient Anonymous() => factory.CreateClient();

    private Task<SchedulingApi> PlatformAdminAsync() =>
        SchedulingApi.SignInAsync(factory, "superadmin@dentalai.local", "SuperAdmin123!");

    private static async Task<StaffInvite> InviteAsync(SchedulingApi api, string url, string email, string role) =>
        await SchedulingApi.ReadAsync<StaffInvite>(await api.Http.PostAsJsonAsync(url,
            new { firstName = "Sam", lastName = "Staff", email, role }, SchedulingApi.Json));

    private Task<HttpResponseMessage> AcceptAsync(string token, string password) =>
        Anonymous().PostAsJsonAsync("/api/auth/accept-invite", new { token, password });

    private Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        Anonymous().PostAsJsonAsync("/api/auth/login", new { email, password });

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        Anonymous().PostAsJsonAsync("/api/auth/refresh-token", new { refreshToken });

    private static async Task<Guid> CreateClinicAsync(SchedulingApi platform) =>
        (await SchedulingApi.ReadAsync<IdDto>(await platform.Http.PostAsJsonAsync("/api/clinics", new
        {
            name = $"Clinic {Guid.NewGuid():N}"[..20], phone = "(212) 555-0100", email = "clinic@test.local",
            address = "1 Main St, New York, NY", timeZone = "America/New_York", country = "US", slotIntervalMinutes = 15
        }, SchedulingApi.Json))).Id;

    /// <summary>A fresh clinic whose first admin has accepted their invite, signed in.</summary>
    private async Task<(Guid ClinicId, SchedulingApi Admin)> NewClinicWithAdminAsync()
    {
        var platform = await PlatformAdminAsync();
        var clinic = await CreateClinicAsync(platform);
        var email = NewEmail();
        var invite = await InviteAsync(platform, $"/api/clinics/{clinic}/staff/invite", email, "ClinicAdmin");
        Assert.Equal(HttpStatusCode.OK, (await AcceptAsync(invite.InviteToken, GoodPassword)).StatusCode);
        return (clinic, await SchedulingApi.SignInAsync(factory, email, GoodPassword));
    }

    private async Task ExpireInviteAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Users.IgnoreQueryFilters().Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.InviteExpiresAtUtc, DateTime.UtcNow.AddMinutes(-1)));
    }
}
