using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DentalAI.Api.IntegrationTests.Infrastructure;
using DentalAI.Application.Services;
using DentalAI.Infrastructure.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DentalAI.Api.IntegrationTests;

public sealed record NotificationSettings(
    bool EmailEnabled, bool SmsEnabled, bool ConfirmationsEnabled, bool RemindersEnabled,
    int FirstReminderHours, int SecondReminderHours, string QuietHoursStart, string QuietHoursEnd, bool EmailLive, bool SmsLive);
public sealed record OutboundMessage(
    Guid Id, string Channel, string Kind, string To, string? Subject, string Body, string Status,
    Guid? PatientId, string? PatientName, Guid? AppointmentId, int? ReminderHours, DateTime NotBeforeUtc, DateTime? SentAtUtc, string? LastError);
public sealed record MessagePage(List<OutboundMessage> Items, int TotalCount);
public sealed record PatientConsent(Guid Id, bool SmsOptIn);

/// <summary>
/// Phase 3: confirmations, reminders, quiet hours, the outbox sender and patients' text replies. The background
/// jobs are off in tests; each test runs the reminder scheduler and the sender itself, at the times it chooses.
/// Each test uses its own clinic (New York) and its own phone number, so STOP replies never touch other tests.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class NotificationTests(ApiFactory factory)
{
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    [Fact]
    public async Task Settings_HaveSafeDefaults_AndOnlyAdminsChangeThem()
    {
        var clinic = await TestClinics.CreateAsync(factory);

        var defaults = await SchedulingApi.ReadAsync<NotificationSettings>(await clinic.Receptionist.Http.GetAsync("/api/notifications/settings"));
        var byReceptionist = await PutSettingsAsync(clinic.Receptionist, Settings());
        var backwards = await PutSettingsAsync(clinic.Admin, Settings(first: 2, second: 24));
        var tooFar = await PutSettingsAsync(clinic.Admin, Settings(first: 96));
        var saved = await SchedulingApi.ReadAsync<NotificationSettings>(await PutSettingsAsync(clinic.Admin,
            Settings(first: 48, second: 0, quietStart: "20:00", quietEnd: "09:00")));

        Assert.Equal((24, 2, "21:00:00", "08:00:00"), (defaults.FirstReminderHours, defaults.SecondReminderHours, defaults.QuietHoursStart, defaults.QuietHoursEnd));
        Assert.False(defaults.EmailLive);   // the Log provider: nothing is really delivered
        Assert.False(defaults.SmsLive);
        Assert.Equal(HttpStatusCode.Forbidden, byReceptionist.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, backwards.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooFar.StatusCode);
        Assert.Equal((48, 0, "20:00:00"), (saved.FirstReminderHours, saved.SecondReminderHours, saved.QuietHoursStart));
    }

    [Fact]
    public async Task Booking_QueuesAConfirmation_ThatTheSenderDelivers()
    {
        var clinic = await TestClinics.CreateAsync(factory);
        var patientId = await CreatePatientAsync(clinic.Admin, "+1 212 555 0161", "ava@example.com", "Email", smsOptIn: false);
        var appointment = await BookAtAsync(clinic.Admin, patientId, hour: 10);

        var queued = Assert.Single(await MessagesAsync(clinic.Admin, appointment.Id));
        await DispatchAsync(DateTime.UtcNow);
        var sent = Assert.Single(await MessagesAsync(clinic.Receptionist, appointment.Id));

        Assert.Equal(("Email", "BookingConfirmation", "ava@example.com", "Pending"), (queued.Channel, queued.Kind, queued.To, queued.Status));
        Assert.Contains("10:00 AM", queued.Subject);   // clinic time, not UTC
        Assert.Equal("Sent", sent.Status);
        Assert.NotNull(sent.SentAtUtc);
        Assert.Equal("Ava Notify", sent.PatientName);
    }

    [Fact]
    public async Task Texts_NeedConsent_AndPatientsWithNoWayToReachThemGetNothing()
    {
        var clinic = await TestClinics.CreateAsync(factory);
        var noConsent = await CreatePatientAsync(clinic.Admin, "+1 212 555 0162", "ben@example.com", "Sms", smsOptIn: true);
        await PostWebhookAsync("+12125550162", "STOP"); // still prefers texts, but no longer consents
        var unreachable = await CreatePatientAsync(clinic.Admin, "+1 212 555 0170", null, "Phone", smsOptIn: false);

        var emailed = await BookAtAsync(clinic.Admin, noConsent, hour: 10);
        var skipped = await BookAtAsync(clinic.Admin, unreachable, hour: 11);

        Assert.Equal("Email", Assert.Single(await MessagesAsync(clinic.Admin, emailed.Id)).Channel);
        Assert.Empty(await MessagesAsync(clinic.Admin, skipped.Id));
    }

    [Fact]
    public async Task Cancelling_WithdrawsPendingMessages_AndSendsANotice()
    {
        var clinic = await TestClinics.CreateAsync(factory);
        var patientId = await CreatePatientAsync(clinic.Admin, "+1 212 555 0163", "cara@example.com", "Email", smsOptIn: false);
        var appointment = await BookAtAsync(clinic.Admin, patientId, hour: 10);

        await clinic.Admin.CancelOkAsync(appointment.Id);
        var messages = await MessagesAsync(clinic.Admin, appointment.Id);

        Assert.Equal("Cancelled", messages.Single(m => m.Kind == "BookingConfirmation").Status);
        Assert.Equal("Pending", messages.Single(m => m.Kind == "Cancelled").Status);
    }

    [Fact]
    public async Task Reminders_AreQueuedWhenDue_OnceEach()
    {
        var clinic = await TestClinics.CreateAsync(factory);
        var patientId = await CreatePatientAsync(clinic.Admin, "+1 212 555 0164", "dan@example.com", "Email", smsOptIn: false);
        var appointment = await BookAtAsync(clinic.Admin, patientId, hour: 10);
        var start = DateTime.SpecifyKind(appointment.StartTime, DateTimeKind.Utc);

        await ScheduleRemindersAsync(start.AddHours(-30));
        var tooEarly = Reminders(await MessagesAsync(clinic.Admin, appointment.Id));
        await ScheduleRemindersAsync(start.AddHours(-23));
        await ScheduleRemindersAsync(start.AddHours(-22));  // runs every minute: must not duplicate
        var dayBefore = Reminders(await MessagesAsync(clinic.Admin, appointment.Id));
        await ScheduleRemindersAsync(start.AddHours(-1));
        var both = Reminders(await MessagesAsync(clinic.Admin, appointment.Id));

        Assert.Empty(tooEarly);
        Assert.Equal([24], dayBefore.Select(m => m.ReminderHours));
        Assert.Equal([2, 24], both.Select(m => m.ReminderHours).Order());
    }

    [Fact]
    public async Task TextReminders_WaitForQuietHoursToEnd_OrAreSkippedIfThatIsTooLate()
    {
        var clinic = await TestClinics.CreateAsync(factory);
        var patientId = await CreatePatientAsync(clinic.Admin, "+1 212 555 0165", null, "Sms", smsOptIn: true);
        var nineAm = await BookAtAsync(clinic.Admin, patientId, hour: 9);
        var sevenAm = await BookAtAsync(clinic.Admin, patientId, hour: 7, daysAhead: 5); // another day: no overlap
        var nineStart = DateTime.SpecifyKind(nineAm.StartTime, DateTimeKind.Utc);
        var sevenStart = DateTime.SpecifyKind(sevenAm.StartTime, DateTimeKind.Utc);

        await ScheduleRemindersAsync(nineStart.AddHours(-2));  // 7 AM: inside the default 9 PM – 8 AM quiet hours
        await ScheduleRemindersAsync(sevenStart.AddHours(-2)); // 5 AM: quiet until after the 7 AM visit

        var delayed = Assert.Single(Reminders(await MessagesAsync(clinic.Admin, nineAm.Id)));
        var skipped = Assert.Single(Reminders(await MessagesAsync(clinic.Admin, sevenAm.Id)));
        Assert.Equal(("Sms", "Pending", 8), (delayed.Channel, delayed.Status, ClinicHour(delayed.NotBeforeUtc)));
        Assert.Equal("Skipped", skipped.Status);
    }

    [Fact]
    public async Task ReplyingStop_WithdrawsSmsConsent()
    {
        var clinic = await TestClinics.CreateAsync(factory);
        var patientId = await CreatePatientAsync(clinic.Admin, "+1 212 555 0166", null, "Sms", smsOptIn: true);

        var reply = await PostWebhookAsync("+12125550166", "Stop");
        var patient = await SchedulingApi.ReadAsync<PatientConsent>(await clinic.Admin.Http.GetAsync($"/api/patients/{patientId}"));

        Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
        Assert.Equal("text/xml", reply.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("<Message>", await reply.Content.ReadAsStringAsync()); // the carrier confirms STOP itself
        Assert.False(patient.SmsOptIn);
    }

    [Fact]
    public async Task Webhook_WithoutAValidSignature_IsRefused()
    {
        var form = new Dictionary<string, string> { ["From"] = "+12125550166", ["Body"] = "STOP" };
        var unsigned = await factory.CreateClient().PostAsync("/api/webhooks/twilio/sms", new FormUrlEncodedContent(form));

        var forged = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/twilio/sms") { Content = new FormUrlEncodedContent(form) };
        forged.Headers.Add("X-Twilio-Signature", TwilioSignature.Compute("not-the-token", ApiFactory.TwilioWebhookUrl, form));
        var wrongToken = await factory.CreateClient().SendAsync(forged);

        Assert.Equal(HttpStatusCode.Forbidden, unsigned.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wrongToken.StatusCode);
    }

    [Fact]
    public async Task ReplyingC_ConfirmsTheAppointmentFromTheLatestText()
    {
        var clinic = await TestClinics.CreateAsync(factory);
        await PutSettingsAsync(clinic.Admin, Settings(quietStart: "00:00", quietEnd: "00:00")); // no quiet hours: send now
        var patientId = await CreatePatientAsync(clinic.Admin, "+1 212 555 0167", null, "Sms", smsOptIn: true);
        var appointment = await BookAtAsync(clinic.Admin, patientId, hour: 10);
        await DispatchAsync(DateTime.UtcNow);

        var reply = await PostWebhookAsync("+12125550167", " c ");
        var confirmed = await SchedulingApi.ReadAsync<AppointmentDto>(await clinic.Admin.Http.GetAsync($"/api/appointments/{appointment.Id}"));

        Assert.Contains("confirmed", await reply.Content.ReadAsStringAsync());
        Assert.Equal("Confirmed", confirmed.Status);
    }

    [Fact]
    public async Task InviteEmails_AreQueued_AndTheLinkIsRemovedAfterSending()
    {
        var clinic = await TestClinics.CreateAsync(factory); // invited an admin and a receptionist

        var queued = (await AllMessagesAsync(clinic.Admin)).Where(m => m.Kind == "StaffInvite").ToList();
        await DispatchAsync(DateTime.UtcNow);
        var sent = (await AllMessagesAsync(clinic.Admin)).Where(m => m.Kind == "StaffInvite").ToList();

        Assert.Equal(2, queued.Count);
        Assert.All(queued, m => Assert.Contains("/auth/accept-invite?token=", m.Body));
        Assert.All(sent, m => Assert.Equal("Sent", m.Status));
        Assert.All(sent, m => Assert.DoesNotContain("token=", m.Body));
    }

    [Fact]
    public async Task MessageLog_IsPerClinic()
    {
        var mine = await TestClinics.CreateAsync(factory);
        var theirs = await TestClinics.CreateAsync(factory);
        var patientId = await CreatePatientAsync(mine.Admin, "+1 212 555 0168", "eve@example.com", "Email", smsOptIn: false);
        var appointment = await BookAtAsync(mine.Admin, patientId, hour: 10);

        Assert.Single(await MessagesAsync(mine.Admin, appointment.Id));
        Assert.Empty(await MessagesAsync(theirs.Admin, appointment.Id));
        Assert.DoesNotContain(await AllMessagesAsync(theirs.Admin), m => m.AppointmentId == appointment.Id);
    }

    [Fact]
    public async Task TestMessages_AreForAdmins_AndCheckTheAddress()
    {
        var clinic = await TestClinics.CreateAsync(factory);

        var sms = await clinic.Admin.Http.PostAsJsonAsync("/api/notifications/test", new { channel = "Sms", to = "(212) 555-0169" }, SchedulingApi.Json);
        var badEmail = await clinic.Admin.Http.PostAsJsonAsync("/api/notifications/test", new { channel = "Email", to = "nobody" }, SchedulingApi.Json);
        var byReceptionist = await clinic.Receptionist.Http.PostAsJsonAsync("/api/notifications/test", new { channel = "Email", to = "a@b.com" }, SchedulingApi.Json);

        var queued = await SchedulingApi.ReadAsync<OutboundMessage>(sms);
        Assert.Equal(("Test", "+12125550169"), (queued.Kind, queued.To)); // a US clinic: the national number gets +1
        Assert.Equal(HttpStatusCode.BadRequest, badEmail.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, byReceptionist.StatusCode);
    }

    // ---- helpers ----

    private static object Settings(int first = 24, int second = 2, string quietStart = "21:00", string quietEnd = "08:00") => new
    {
        emailEnabled = true, smsEnabled = true, confirmationsEnabled = true, remindersEnabled = true,
        firstReminderHours = first, secondReminderHours = second, quietHoursStart = quietStart, quietHoursEnd = quietEnd
    };

    private static Task<HttpResponseMessage> PutSettingsAsync(SchedulingApi api, object settings) =>
        api.Http.PutAsJsonAsync("/api/notifications/settings", settings, SchedulingApi.Json);

    private static async Task<Guid> CreatePatientAsync(SchedulingApi api, string phone, string? email, string preferred, bool smsOptIn) =>
        (await SchedulingApi.ReadAsync<IdDto>(await api.Http.PostAsJsonAsync("/api/patients", new Dictionary<string, object?>
        {
            ["firstName"] = "Ava", ["lastName"] = "Notify", ["phone"] = phone, ["email"] = email,
            ["preferredContactMethod"] = preferred, ["smsOptIn"] = smsOptIn
        }, SchedulingApi.Json))).Id;

    /// <summary>Books a fresh doctor at the given clinic hour (New York), a few days ahead.</summary>
    private static async Task<AppointmentDto> BookAtAsync(SchedulingApi api, Guid patientId, int hour, int daysAhead = 3)
    {
        var doctorId = await api.CreateDoctorAsync();
        var serviceId = await api.CreateServiceAsync();
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, NewYork)).AddDays(daysAhead);
        var slot = (await api.GetSlotsAsync(doctorId, serviceId, date)).First(s => ClinicHour(s.StartTimeUtc) == hour && ClinicMinute(s.StartTimeUtc) == 0);
        return await api.BookOkAsync(doctorId, serviceId, patientId, slot.StartTimeUtc);
    }

    private static int ClinicHour(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), NewYork).Hour;
    private static int ClinicMinute(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), NewYork).Minute;

    private static List<OutboundMessage> Reminders(List<OutboundMessage> messages) => messages.Where(m => m.Kind == "Reminder").ToList();

    private static async Task<List<OutboundMessage>> MessagesAsync(SchedulingApi api, Guid appointmentId) =>
        (await SchedulingApi.ReadAsync<MessagePage>(await api.Http.GetAsync($"/api/notifications/messages?appointmentId={appointmentId}&pageSize=100"))).Items;

    private static async Task<List<OutboundMessage>> AllMessagesAsync(SchedulingApi api) =>
        (await SchedulingApi.ReadAsync<MessagePage>(await api.Http.GetAsync("/api/notifications/messages?pageSize=100"))).Items;

    private async Task ScheduleRemindersAsync(DateTime nowUtc)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ReminderScheduler>().RunOnceAsync(nowUtc, CancellationToken.None);
    }

    private async Task DispatchAsync(DateTime nowUtc)
    {
        // Loop: other tests' messages share the outbox, and a batch is 20.
        for (var i = 0; i < 50; i++)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            if (await scope.ServiceProvider.GetRequiredService<MessageDispatcher>().RunOnceAsync(nowUtc, CancellationToken.None) == 0) return;
        }
    }

    private async Task<HttpResponseMessage> PostWebhookAsync(string from, string body)
    {
        var form = new Dictionary<string, string> { ["MessageSid"] = $"SM{Guid.NewGuid():N}", ["From"] = from, ["To"] = "+12125550100", ["Body"] = body };
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/twilio/sms") { Content = new FormUrlEncodedContent(form) };
        request.Headers.Add("X-Twilio-Signature", TwilioSignature.Compute(ApiFactory.TwilioTestAuthToken, ApiFactory.TwilioWebhookUrl, form));
        return await factory.CreateClient().SendAsync(request);
    }
}
