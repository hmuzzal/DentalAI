using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using DentalAI.Api.IntegrationTests.Infrastructure;
using DentalAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Entities = DentalAI.Domain.Entities;

namespace DentalAI.Api.IntegrationTests;

public sealed record PortalSettings(bool Enabled, string Slug, string Path, bool AllowNewPatients, bool AutoConfirm,
    int MinNoticeHours, int MaxDaysAhead, int CancelCutoffHours, int MaxUpcomingAppointments);
public sealed record PortalClinic(string Name, string Slug, string TimeZone, bool AllowNewPatients);
public sealed record PortalStart(string Channel, string Contact);
public sealed record PortalChoice(Guid Id, string DisplayName);
public sealed record PortalVerify(string Ticket, string Channel, string Contact, List<PortalChoice> Patients, bool CanRegister);
public sealed record PortalPatientSummary(Guid Id, string FirstName, string LastName);
public sealed record PortalSession(string AccessToken, string RefreshToken, PortalPatientSummary Patient, PortalClinic Clinic);
public sealed record PortalProfile(Guid Id, string FirstName, string Phone, string? Email, bool SmsOptIn);
public sealed record PortalService(Guid Id, string Name);
public sealed record PortalAppointment(Guid Id, Guid ServiceId, DateTime StartTime, string Status, string Source, bool CanCancel,
    bool CanReschedule);
public sealed record PortalInvoiceSummary(Guid Id, string? Number, string Status, decimal Balance);
public sealed record PortalInvoiceList(List<PortalInvoiceSummary> Items, int TotalCount);
public sealed record PortalInvoice(Guid Id, string Status, decimal Total, List<JsonElement> Lines, List<JsonElement> Payments);
public sealed record StaffAppointment(Guid Id, string Status, string Source);

/// <summary>
/// The patient portal: passwordless sign-in, token separation from staff, each patient seeing only their own
/// records, and the clinic's booking rules. Every test uses its own clinic.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed partial class PortalTests(ApiFactory factory)
{
    // ---- Settings ----

    [Fact]
    public async Task Portal_IsOffUntilAnAdminTurnsItOn_AndOnlyAdminsManageIt()
    {
        var clinic = await TestClinics.CreateAsync(factory);
        var settings = await SchedulingApi.ReadAsync<PortalSettings>(await clinic.Admin.Http.GetAsync("/api/portalSettings"));

        Assert.False(settings.Enabled);
        Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", settings.Slug);
        Assert.Equal($"/p/{settings.Slug}", settings.Path);
        Assert.Equal(HttpStatusCode.NotFound, (await Anonymous().GetAsync($"/api/portal/clinics/{settings.Slug}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clinic.Receptionist.Http.GetAsync("/api/portalSettings")).StatusCode);

        await SaveSettingsAsync(clinic, settings with { Enabled = true });
        var info = await SchedulingApi.ReadAsync<PortalClinic>(await Anonymous().GetAsync($"/api/portal/clinics/{settings.Slug}"));
        Assert.Equal("America/New_York", info.TimeZone);

        var other = await TestClinics.CreateAsync(factory);
        var otherSettings = await SchedulingApi.ReadAsync<PortalSettings>(await other.Admin.Http.GetAsync("/api/portalSettings"));
        var taken = await other.Admin.Http.PutAsJsonAsync("/api/portalSettings", otherSettings with { Slug = settings.Slug }, SchedulingApi.Json);
        var invalid = await other.Admin.Http.PutAsJsonAsync("/api/portalSettings", otherSettings with { Slug = "Not Valid!" }, SchedulingApi.Json);
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    // ---- Sign-in ----

    [Fact]
    public async Task ExistingPatient_SignsInWithACode_AndTheTicketWorksOnce()
    {
        var portal = await PortalClinicAsync();
        var email = NewEmail();
        var patientId = await CreatePatientAsync(portal.Clinic, "Jane", NewPhone(), email);

        var start = await Anonymous().PostAsJsonAsync($"/api/portal/clinics/{portal.Slug}/auth/start", new { contact = email.ToUpperInvariant() });
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var code = await LatestCodeAsync(email);

        var wrong = await VerifyAsync(portal.Slug, email, code == "000000" ? "111111" : "000000");
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        var verified = await SchedulingApi.ReadAsync<PortalVerify>(await VerifyAsync(portal.Slug, email, code));
        Assert.Equal("Email", verified.Channel);
        Assert.Equal("Jane T.", Assert.Single(verified.Patients).DisplayName);

        var session = await SelectAsync(portal.Slug, verified.Ticket, patientId);
        var reused = await Anonymous().PostAsJsonAsync($"/api/portal/clinics/{portal.Slug}/auth/select",
            new { ticket = verified.Ticket, patientId });
        var me = await SchedulingApi.ReadAsync<PortalProfile>(await Patient(session).GetAsync("/api/portal/me"));

        Assert.Equal(patientId, session.Patient.Id);
        Assert.Equal(patientId, me.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
    }

    [Fact]
    public async Task Codes_RunOutAfterFiveWrongTries_AndExpire()
    {
        var portal = await PortalClinicAsync();
        var email = NewEmail();
        await CreatePatientAsync(portal.Clinic, "Ann", NewPhone(), email);

        await StartAsync(portal.Slug, email);
        var code = await LatestCodeAsync(email);
        for (var i = 0; i < 5; i++) await VerifyAsync(portal.Slug, email, code == "000000" ? "111111" : "000000");
        Assert.Equal(HttpStatusCode.BadRequest, (await VerifyAsync(portal.Slug, email, code)).StatusCode);

        await StartAsync(portal.Slug, email);
        var second = await LatestCodeAsync(email);
        await WithDbAsync(async db =>
        {
            var row = await db.PortalSignInCodes.IgnoreQueryFilters().Where(x => x.Contact == email)
                .OrderByDescending(x => x.CreatedDate).FirstAsync();
            row.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.BadRequest, (await VerifyAsync(portal.Slug, email, second)).StatusCode);
    }

    [Fact]
    public async Task Codes_AreLimitedPerContact()
    {
        var portal = await PortalClinicAsync();
        var email = NewEmail();

        for (var i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.OK, (await StartAsync(portal.Slug, email)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await StartAsync(portal.Slug, email)).StatusCode);
    }

    [Fact]
    public async Task UnknownContacts_GetTheSameAnswer_AndCanRegisterOnlyWhenTheClinicAllowsIt()
    {
        var portal = await PortalClinicAsync(s => s with { AllowNewPatients = false });
        var stranger = NewEmail();

        var closed = await StartAsync(portal.Slug, stranger);
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        Assert.Null(await LatestCodeOrNullAsync(stranger)); // nothing sent, but the answer doesn't say so

        await SaveSettingsAsync(portal.Clinic, portal.Settings with { Enabled = true, AllowNewPatients = true });
        await StartAsync(portal.Slug, stranger);
        var verified = await SchedulingApi.ReadAsync<PortalVerify>(await VerifyAsync(portal.Slug, stranger, await LatestCodeAsync(stranger)));
        Assert.Empty(verified.Patients);
        Assert.True(verified.CanRegister);

        var missingPhone = await Anonymous().PostAsJsonAsync($"/api/portal/clinics/{portal.Slug}/auth/register",
            new { ticket = verified.Ticket, firstName = "New", lastName = "Person", dateOfBirth = "1990-04-01" });
        Assert.Equal(HttpStatusCode.BadRequest, missingPhone.StatusCode);

        var session = await SchedulingApi.ReadAsync<PortalSession>(await Anonymous().PostAsJsonAsync(
            $"/api/portal/clinics/{portal.Slug}/auth/register",
            new { ticket = verified.Ticket, firstName = "New", lastName = "Person", dateOfBirth = "1990-04-01", phone = "(212) 555-0147" }));
        var me = await SchedulingApi.ReadAsync<PortalProfile>(await Patient(session).GetAsync("/api/portal/me"));
        Assert.Equal(stranger, me.Email);
        Assert.Equal("+12125550147", me.Phone);

        // The new patient is in the clinic's list for the front desk.
        var staffView = await portal.Clinic.Admin.Http.GetAsync($"/api/patients/{session.Patient.Id}");
        Assert.Equal(HttpStatusCode.OK, staffView.StatusCode);
    }

    [Fact]
    public async Task SharedPhone_LetsThePersonChooseWhoIsBooking()
    {
        var portal = await PortalClinicAsync();
        var phone = NewPhone();
        var parent = await CreatePatientAsync(portal.Clinic, "Paula", phone, null);
        var child = await CreatePatientAsync(portal.Clinic, "Chris", phone, null);

        await StartAsync(portal.Slug, phone);
        var e164 = await E164Async(portal.Clinic, child);
        var verified = await SchedulingApi.ReadAsync<PortalVerify>(await VerifyAsync(portal.Slug, phone, await LatestCodeAsync(e164)));
        Assert.Equal("Sms", verified.Channel);
        Assert.Equal(new[] { child, parent }.OrderBy(x => x), verified.Patients.Select(p => p.Id).OrderBy(x => x));

        var session = await SelectAsync(portal.Slug, verified.Ticket, child);
        Assert.Equal("Chris", (await SchedulingApi.ReadAsync<PortalProfile>(await Patient(session).GetAsync("/api/portal/me"))).FirstName);
    }

    [Fact]
    public async Task StaffNeverSeeSignInCodes_InTheMessageLog()
    {
        var portal = await PortalClinicAsync();
        var email = NewEmail();
        await CreatePatientAsync(portal.Clinic, "Hidden", NewPhone(), email);
        await StartAsync(portal.Slug, email);
        var code = await LatestCodeAsync(email);

        var log = await portal.Clinic.Admin.Http.GetStringAsync("/api/notifications/messages?pageSize=100");
        Assert.Contains("PortalCode", log);
        Assert.DoesNotContain(code, log);
    }

    // ---- Tokens ----

    [Fact]
    public async Task PatientTokens_DontWorkOnStaffEndpoints_AndStaffTokensDontWorkOnThePortal()
    {
        var portal = await PortalClinicAsync();
        var session = await SignInNewPatientAsync(portal, "Tom");
        var patient = Patient(session);

        Assert.Equal(HttpStatusCode.Unauthorized, (await patient.GetAsync("/api/patients")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await patient.GetAsync("/api/appointments")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await patient.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await patient.GetAsync("/api/lookups/time-zones")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await portal.Clinic.Admin.Http.GetAsync("/api/portal/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Anonymous().GetAsync("/api/portal/me")).StatusCode);
    }

    [Fact]
    public async Task Refresh_RotatesTheToken_AndSignOutEndsTheSession()
    {
        var portal = await PortalClinicAsync();
        var session = await SignInNewPatientAsync(portal, "Rita");

        var refreshed = await SchedulingApi.ReadAsync<PortalSession>(
            await Anonymous().PostAsJsonAsync("/api/portal/auth/refresh", new { refreshToken = session.RefreshToken }));
        var oldAgain = await Anonymous().PostAsJsonAsync("/api/portal/auth/refresh", new { refreshToken = session.RefreshToken });
        Assert.NotEqual(session.RefreshToken, refreshed.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, oldAgain.StatusCode);

        await Anonymous().PostAsJsonAsync("/api/portal/auth/logout", new { refreshToken = refreshed.RefreshToken });
        var afterLogout = await Anonymous().PostAsJsonAsync("/api/portal/auth/refresh", new { refreshToken = refreshed.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [Fact]
    public async Task TurningThePortalOff_EndsPatientSessions()
    {
        var portal = await PortalClinicAsync();
        var session = await SignInNewPatientAsync(portal, "Olga");
        Assert.Equal(HttpStatusCode.OK, (await Patient(session).GetAsync("/api/portal/me")).StatusCode);

        await SaveSettingsAsync(portal.Clinic, portal.Settings with { Enabled = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await Patient(session).GetAsync("/api/portal/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Anonymous().PostAsJsonAsync("/api/portal/auth/refresh", new { refreshToken = session.RefreshToken })).StatusCode);
    }

    // ---- Booking ----

    [Fact]
    public async Task Patient_BooksAnOnlineService_WhichStaffSeeAsAnOnlinePendingBooking()
    {
        var portal = await PortalClinicAsync();
        var (doctorId, serviceId) = await DoctorAndServiceAsync(portal);
        var hidden = await portal.Clinic.Admin.Http.PostAsJsonAsync("/api/services", new
        {
            name = $"Surgery {Guid.NewGuid():N}"[..20], price = 900m, durationMinutes = 60, isActive = true, bookableOnline = false
        }, SchedulingApi.Json);
        var hiddenId = (await SchedulingApi.ReadAsync<IdDto>(hidden)).Id;
        var session = await SignInNewPatientAsync(portal, "Bea");
        var patient = Patient(session);

        var services = await SchedulingApi.ReadAsync<List<PortalService>>(await patient.GetAsync("/api/portal/services"));
        Assert.Contains(services, s => s.Id == serviceId);
        Assert.DoesNotContain(services, s => s.Id == hiddenId);
        Assert.Equal(HttpStatusCode.NotFound,
            (await patient.GetAsync($"/api/portal/slots?doctorId={doctorId}&serviceId={hiddenId}&date={SchedulingApi.FutureClinicDate():yyyy-MM-dd}")).StatusCode);

        var slot = (await SlotsAsync(patient, doctorId, serviceId, SchedulingApi.FutureClinicDate()))[4];
        var booked = await SchedulingApi.ReadAsync<PortalAppointment>(await BookAsync(patient, doctorId, serviceId, slot.StartTimeUtc));
        Assert.Equal("Pending", booked.Status);
        Assert.Equal("Portal", booked.Source);
        Assert.True(booked.CanCancel);

        var staffView = await SchedulingApi.ReadAsync<StaffAppointment>(await portal.Clinic.Admin.Http.GetAsync($"/api/appointments/{booked.Id}"));
        Assert.Equal("Portal", staffView.Source);

        // The same engine and lock as the front desk: the slot is gone, and taking it again is a conflict.
        var again = await SlotsAsync(patient, doctorId, serviceId, SchedulingApi.FutureClinicDate());
        Assert.DoesNotContain(again, s => s.StartTimeUtc == slot.StartTimeUtc);
        var otherPatient = Patient(await SignInNewPatientAsync(portal, "Cleo"));
        Assert.Equal(HttpStatusCode.Conflict, (await BookAsync(otherPatient, doctorId, serviceId, slot.StartTimeUtc)).StatusCode);
    }

    [Fact]
    public async Task ClinicRules_Notice_Horizon_Limit_AndAutoConfirm_AreEnforced()
    {
        var portal = await PortalClinicAsync(s => s with { MinNoticeHours = 48, MaxDaysAhead = 10, MaxUpcomingAppointments = 1, AutoConfirm = true });
        var (doctorId, serviceId) = await DoctorAndServiceAsync(portal);
        var patient = Patient(await SignInNewPatientAsync(portal, "Dina"));

        var tooSoon = DateTime.UtcNow.AddHours(5);
        Assert.Equal(HttpStatusCode.BadRequest, (await BookAsync(patient, doctorId, serviceId, tooSoon)).StatusCode);
        Assert.Empty(await SlotsAsync(patient, doctorId, serviceId, SchedulingApi.FutureClinicDate(20)));

        var slot = (await SlotsAsync(patient, doctorId, serviceId, SchedulingApi.FutureClinicDate(5)))[0];
        Assert.Equal(HttpStatusCode.BadRequest, (await BookAsync(patient, doctorId, serviceId, slot.StartTimeUtc.AddDays(15))).StatusCode);

        var booked = await SchedulingApi.ReadAsync<PortalAppointment>(await BookAsync(patient, doctorId, serviceId, slot.StartTimeUtc));
        Assert.Equal("Confirmed", booked.Status);

        var second = (await SlotsAsync(patient, doctorId, serviceId, SchedulingApi.FutureClinicDate(6)))[0];
        Assert.Equal(HttpStatusCode.Conflict, (await BookAsync(patient, doctorId, serviceId, second.StartTimeUtc)).StatusCode);
    }

    [Fact]
    public async Task Patient_ReschedulesAndCancels_UntilTheCutoff()
    {
        var portal = await PortalClinicAsync(s => s with { CancelCutoffHours = 0, MinNoticeHours = 0 });
        var (doctorId, serviceId) = await DoctorAndServiceAsync(portal);
        var patient = Patient(await SignInNewPatientAsync(portal, "Eva"));
        var day = SchedulingApi.FutureClinicDate();
        var slots = await SlotsAsync(patient, doctorId, serviceId, day);

        var booked = await SchedulingApi.ReadAsync<PortalAppointment>(await BookAsync(patient, doctorId, serviceId, slots[2].StartTimeUtc));
        var moveSlots = await SchedulingApi.ReadAsync<List<SlotDto>>(await patient.GetAsync(
            $"/api/portal/slots?doctorId={doctorId}&serviceId={serviceId}&date={day:yyyy-MM-dd}&rescheduleAppointmentId={booked.Id}"));
        Assert.Contains(moveSlots, s => s.StartTimeUtc == slots[2].StartTimeUtc); // doesn't block itself

        var moved = await SchedulingApi.ReadAsync<PortalAppointment>(await patient.PostAsJsonAsync(
            $"/api/portal/appointments/{booked.Id}/reschedule", new { startTime = slots[6].StartTimeUtc }, SchedulingApi.Json));
        Assert.Equal(slots[6].StartTimeUtc, moved.StartTime);

        // A cutoff longer than the time left: no more online changes.
        await SaveSettingsAsync(portal.Clinic, portal.Settings with { Enabled = true, CancelCutoffHours = 168, MinNoticeHours = 0 });
        var list = await SchedulingApi.ReadAsync<List<PortalAppointment>>(await patient.GetAsync("/api/portal/appointments"));
        Assert.False(list.Single(a => a.Id == booked.Id).CanCancel);
        Assert.Equal(HttpStatusCode.Conflict, (await patient.PostAsync($"/api/portal/appointments/{booked.Id}/cancel", null)).StatusCode);

        await SaveSettingsAsync(portal.Clinic, portal.Settings with { Enabled = true, CancelCutoffHours = 0, MinNoticeHours = 0 });
        var cancelled = await SchedulingApi.ReadAsync<PortalAppointment>(await patient.PostAsync($"/api/portal/appointments/{booked.Id}/cancel", null));
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.False(cancelled.CanCancel);
    }

    // ---- Isolation ----

    [Fact]
    public async Task Patients_SeeOnlyTheirOwnAppointmentsAndIssuedInvoices()
    {
        var portal = await PortalClinicAsync(s => s with { MinNoticeHours = 0 });
        var (doctorId, serviceId) = await DoctorAndServiceAsync(portal);
        var alice = await SignInNewPatientAsync(portal, "Alice");
        var bob = await SignInNewPatientAsync(portal, "Bob");
        var slots = await SlotsAsync(Patient(alice), doctorId, serviceId, SchedulingApi.FutureClinicDate());

        var bobsVisit = await SchedulingApi.ReadAsync<PortalAppointment>(await BookAsync(Patient(bob), doctorId, serviceId, slots[1].StartTimeUtc));
        var bobsInvoice = await IssuedInvoiceAsync(portal.Clinic, bob.Patient.Id);
        var alicesInvoice = await IssuedInvoiceAsync(portal.Clinic, alice.Patient.Id);
        var alicesDraft = await DraftInvoiceAsync(portal.Clinic, alice.Patient.Id);
        var a = Patient(alice);

        Assert.DoesNotContain(await SchedulingApi.ReadAsync<List<PortalAppointment>>(await a.GetAsync("/api/portal/appointments")),
            x => x.Id == bobsVisit.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await a.PostAsync($"/api/portal/appointments/{bobsVisit.Id}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.PostAsJsonAsync($"/api/portal/appointments/{bobsVisit.Id}/reschedule",
            new { startTime = slots[5].StartTimeUtc }, SchedulingApi.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.GetAsync(
            $"/api/portal/slots?doctorId={doctorId}&serviceId={serviceId}&date={SchedulingApi.FutureClinicDate():yyyy-MM-dd}&rescheduleAppointmentId={bobsVisit.Id}")).StatusCode);

        var invoices = await SchedulingApi.ReadAsync<PortalInvoiceList>(await a.GetAsync("/api/portal/invoices"));
        Assert.Equal(alicesInvoice, Assert.Single(invoices.Items).Id);
        Assert.Equal(HttpStatusCode.NotFound, (await a.GetAsync($"/api/portal/invoices/{bobsInvoice}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.GetAsync($"/api/portal/invoices/{alicesDraft}")).StatusCode);

        var body = await a.GetStringAsync($"/api/portal/invoices/{alicesInvoice}");
        var invoice = JsonSerializer.Deserialize<Envelope<PortalInvoice>>(body, SchedulingApi.Json)!.Data!;
        Assert.Equal("Issued", invoice.Status);
        Assert.DoesNotContain("recordedBy", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Patients_OfOneClinic_CantSeeAnotherClinicsPortalData()
    {
        var first = await PortalClinicAsync(s => s with { MinNoticeHours = 0 });
        var second = await PortalClinicAsync(s => s with { MinNoticeHours = 0 });
        var (doctorId, serviceId) = await DoctorAndServiceAsync(second);
        var patient = Patient(await SignInNewPatientAsync(first, "Fay"));

        var services = await SchedulingApi.ReadAsync<List<PortalService>>(await patient.GetAsync("/api/portal/services"));
        Assert.DoesNotContain(services, s => s.Id == serviceId);
        Assert.Equal(HttpStatusCode.NotFound, (await patient.GetAsync(
            $"/api/portal/slots?doctorId={doctorId}&serviceId={serviceId}&date={SchedulingApi.FutureClinicDate():yyyy-MM-dd}")).StatusCode);
    }

    // ---- Helpers ----

    private sealed record TestPortal(TestClinic Clinic, PortalSettings Settings, string Slug);

    private async Task<TestPortal> PortalClinicAsync(Func<PortalSettings, PortalSettings>? configure = null)
    {
        var clinic = await TestClinics.CreateAsync(factory);
        var settings = await SchedulingApi.ReadAsync<PortalSettings>(await clinic.Admin.Http.GetAsync("/api/portalSettings"));
        settings = (configure ?? (s => s))(settings with { Enabled = true });
        await SaveSettingsAsync(clinic, settings);
        return new TestPortal(clinic, settings, settings.Slug);
    }

    private static async Task SaveSettingsAsync(TestClinic clinic, PortalSettings settings) =>
        await SchedulingApi.ReadAsync<PortalSettings>(await clinic.Admin.Http.PutAsJsonAsync("/api/portalSettings", settings, SchedulingApi.Json));

    private static async Task<(Guid DoctorId, Guid ServiceId)> DoctorAndServiceAsync(TestPortal portal) =>
        (await portal.Clinic.Admin.CreateDoctorAsync(), await portal.Clinic.Admin.CreateServiceAsync());

    private static async Task<Guid> CreatePatientAsync(TestClinic clinic, string firstName, string phone, string? email) =>
        (await SchedulingApi.ReadAsync<IdDto>(await clinic.Admin.Http.PostAsJsonAsync("/api/patients", new
        {
            firstName,
            lastName = "Test",
            phone,
            email
        }, SchedulingApi.Json))).Id;

    /// <summary>A brand-new patient registered through the portal with a fresh email, signed in.</summary>
    private async Task<PortalSession> SignInNewPatientAsync(TestPortal portal, string firstName)
    {
        var email = NewEmail();
        await StartAsync(portal.Slug, email);
        var verified = await SchedulingApi.ReadAsync<PortalVerify>(await VerifyAsync(portal.Slug, email, await LatestCodeAsync(email)));
        return await SchedulingApi.ReadAsync<PortalSession>(await Anonymous().PostAsJsonAsync(
            $"/api/portal/clinics/{portal.Slug}/auth/register",
            new { ticket = verified.Ticket, firstName, lastName = "Portal", dateOfBirth = "1985-06-15", phone = NewPhone() }));
    }

    private Task<HttpResponseMessage> StartAsync(string slug, string contact) =>
        Anonymous().PostAsJsonAsync($"/api/portal/clinics/{slug}/auth/start", new { contact });

    private Task<HttpResponseMessage> VerifyAsync(string slug, string contact, string code) =>
        Anonymous().PostAsJsonAsync($"/api/portal/clinics/{slug}/auth/verify", new { contact, code });

    private async Task<PortalSession> SelectAsync(string slug, string ticket, Guid patientId) =>
        await SchedulingApi.ReadAsync<PortalSession>(await Anonymous().PostAsJsonAsync(
            $"/api/portal/clinics/{slug}/auth/select", new { ticket, patientId }));

    private static async Task<List<SlotDto>> SlotsAsync(HttpClient patient, Guid doctorId, Guid serviceId, DateOnly date) =>
        await SchedulingApi.ReadAsync<List<SlotDto>>(await patient.GetAsync(
            $"/api/portal/slots?doctorId={doctorId}&serviceId={serviceId}&date={date:yyyy-MM-dd}"));

    private static Task<HttpResponseMessage> BookAsync(HttpClient patient, Guid doctorId, Guid serviceId, DateTime startUtc) =>
        patient.PostAsJsonAsync("/api/portal/appointments", new { doctorId, serviceId, startTime = startUtc }, SchedulingApi.Json);

    private static async Task<Guid> DraftInvoiceAsync(TestClinic clinic, Guid patientId) =>
        (await SchedulingApi.ReadAsync<IdDto>(await clinic.Admin.Http.PostAsJsonAsync("/api/invoices", new
        {
            patientId,
            lines = new[] { new { description = "Exam", quantity = 1, unitPrice = 100m } }
        }, SchedulingApi.Json))).Id;

    private static async Task<Guid> IssuedInvoiceAsync(TestClinic clinic, Guid patientId)
    {
        var id = await DraftInvoiceAsync(clinic, patientId);
        await SchedulingApi.ReadAsync<JsonElement>(await clinic.Admin.Http.PostAsync($"/api/invoices/{id}/issue", null));
        return id;
    }

    /// <summary>The 6-digit code in the newest sign-in message to <paramref name="to"/> (read before it's sent and redacted).</summary>
    private async Task<string> LatestCodeAsync(string to) =>
        await LatestCodeOrNullAsync(to) ?? throw new InvalidOperationException($"No sign-in code was sent to {to}.");

    private async Task<string?> LatestCodeOrNullAsync(string to)
    {
        string? body = null;
        await WithDbAsync(async db => body = await db.OutboundMessages.IgnoreQueryFilters()
            .Where(m => m.To == to && m.Kind == Entities.MessageKind.PortalCode)
            .OrderByDescending(m => m.CreatedDate)
            .Select(m => m.Body)
            .FirstOrDefaultAsync());
        return body is null ? null : Regex.Match(body, @"\b\d{6}\b").Value;
    }

    private async Task<string> E164Async(TestClinic clinic, Guid patientId) =>
        (await SchedulingApi.ReadAsync<PortalProfile>(await clinic.Admin.Http.GetAsync($"/api/patients/{patientId}"))).Phone;

    private async Task WithDbAsync(Func<ApplicationDbContext, Task> work)
    {
        using var scope = factory.Services.CreateScope();
        await work(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    /// <summary>The assistant's scripted model (see PortalAssistantTests).</summary>
    private ScriptedChatModel ChatModel => factory.ChatModel;

    private HttpClient Anonymous() => factory.CreateClient();

    private HttpClient Patient(PortalSession session)
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return http;
    }

    private static string NewEmail() => $"patient.{Guid.NewGuid():N}@test.local";

    /// <summary>A random valid New York number (212-555-01xx is reserved for fiction but not "valid" for libphonenumber).</summary>
    private static string NewPhone() => $"(212) {Random.Shared.Next(200, 999)}-{Random.Shared.Next(1000, 9999)}";
}
