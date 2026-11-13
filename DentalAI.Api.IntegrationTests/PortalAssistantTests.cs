using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DentalAI.Api.IntegrationTests.Infrastructure;
using DentalAI.Application.Common.Ai;
using Xunit;
using static DentalAI.Api.IntegrationTests.Infrastructure.ScriptedChatModel;

namespace DentalAI.Api.IntegrationTests;

public sealed record AssistantStatus(bool Enabled, string Provider, int MaxMessageLength);
public sealed record AssistantReply(Guid ConversationId, string Reply, bool AppointmentsChanged);

/// <summary>
/// The AI assistant in the portal, with a scripted model in place of Ollama or Gemini. Whatever the model asks for,
/// it goes through the portal's own booking code: the clinic's rules and the patient's own records only.
/// </summary>
public sealed partial class PortalTests
{
    [Fact]
    public async Task Assistant_FindsATime_AndBooksItAfterThePatientSaysYes()
    {
        var portal = await PortalClinicAsync(s => s with { MinNoticeHours = 0 });
        await DoctorAndServiceAsync(portal);
        var session = await SignInNewPatientAsync(portal, "Gail");
        var patient = Patient(session);
        var day = SchedulingApi.FutureClinicDate();
        string? offered = null;

        ChatModel.Script(
            _ => Call(("list_services", new { }), ("list_dentists", new { })),
            r => Call(("find_open_times", new
            {
                service_id = FirstId(LastResult(r, "list_services"), "services"),
                dentist_id = FirstId(LastResult(r, "list_dentists"), "dentists"),
                date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            })),
            r =>
            {
                offered = LastResult(r, "find_open_times").GetProperty("dentists")[0].GetProperty("times")[0].GetProperty("start").GetString();
                return Say($"I have {offered}. Shall I book it?");
            });

        var first = await SendAsync(patient, null, "I need a check-up");
        Assert.False(first.AppointmentsChanged);
        Assert.NotNull(offered);
        Assert.Contains("Gail", ChatModel.Requests[0].SystemPrompt);
        // Day names are looked up, not worked out: the next two weeks are listed with their dates.
        Assert.Contains($"= {day:yyyy-MM-dd}", ChatModel.Requests[0].SystemPrompt);

        // Picking a time is not a yes: the model books straight away, but the server only checks it and asks.
        ChatModel.Script(
            r =>
            {
                // The earlier tool results are still in the history the model sees (kept on the server).
                Assert.Contains(r.Messages, m => m.Role == ChatRole.Tool && m.ToolName == "find_open_times");
                return Call(("book_appointment", new { service_id = "S1", dentist_id = "D1", start = offered }));
            },
            r =>
            {
                var check = LastResult(r, "book_appointment");
                Assert.False(check.GetProperty("ok").GetBoolean());
                Assert.True(check.GetProperty("needs_confirmation").GetBoolean());
                Assert.StartsWith("Book ", check.GetProperty("change").GetString());
                return Say("Shall I book that for you?");
            });

        var picked = await SendAsync(patient, first.ConversationId, "The first one please");
        Assert.False(picked.AppointmentsChanged);
        Assert.Empty(await SchedulingApi.ReadAsync<List<PortalAppointment>>(await patient.GetAsync("/api/portal/appointments")));

        ChatModel.Script(
            _ => Call(("book_appointment", new { service_id = "S1", dentist_id = "D1", start = offered })),
            r =>
            {
                Assert.True(LastResult(r, "book_appointment").GetProperty("ok").GetBoolean());
                return Say("You're booked.");
            });

        var second = await SendAsync(patient, first.ConversationId, "Yes please");
        Assert.True(second.AppointmentsChanged);
        Assert.Equal(first.ConversationId, second.ConversationId);
        Assert.Equal("You're booked.", second.Reply);

        var visit = Assert.Single(await SchedulingApi.ReadAsync<List<PortalAppointment>>(await patient.GetAsync("/api/portal/appointments")));
        Assert.Equal("Portal", visit.Source);
        Assert.Equal("Pending", visit.Status);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(session.Clinic.TimeZone);
        Assert.Equal(offered, TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(visit.StartTime, DateTimeKind.Utc), zone)
            .ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Assistant_CantBreakClinicRules_OrTouchAnotherPatientsVisit()
    {
        var portal = await PortalClinicAsync(s => s with { MinNoticeHours = 48 });
        var (doctorId, serviceId) = await DoctorAndServiceAsync(portal);
        var bob = Patient(await SignInNewPatientAsync(portal, "Bob"));
        var bobsSlot = (await SlotsAsync(bob, doctorId, serviceId, SchedulingApi.FutureClinicDate(5)))[0];
        var bobsVisit = await SchedulingApi.ReadAsync<PortalAppointment>(await BookAsync(bob, doctorId, serviceId, bobsSlot.StartTimeUtc));
        var alice = await SignInNewPatientAsync(portal, "Alice");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(alice.Clinic.TimeZone);
        var tooSoon = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow.AddHours(5), zone).ToString("yyyy-MM-dd'T'HH:00", CultureInfo.InvariantCulture);

        ChatModel.Script(
            _ => Call(
                ("book_appointment", new { service_id = serviceId.ToString(), dentist_id = doctorId.ToString(), start = tooSoon }),
                ("cancel_appointment", new { appointment_id = bobsVisit.Id.ToString() }),
                ("no_such_tool", new { })),
            r =>
            {
                var book = LastResult(r, "book_appointment");
                Assert.False(book.GetProperty("ok").GetBoolean());
                Assert.Contains("notice", book.GetProperty("error").GetString());
                var cancel = LastResult(r, "cancel_appointment");
                Assert.False(cancel.GetProperty("ok").GetBoolean());
                Assert.Contains("not found", cancel.GetProperty("error").GetString());
                Assert.False(LastResult(r, "no_such_tool").GetProperty("ok").GetBoolean());
                return Say("Sorry, I can't do that.");
            });

        var reply = await SendAsync(Patient(alice), null, "Book me in 5 hours and cancel Bob's visit");
        Assert.False(reply.AppointmentsChanged);

        Assert.Empty(await SchedulingApi.ReadAsync<List<PortalAppointment>>(await Patient(alice).GetAsync("/api/portal/appointments")));
        var bobs = await SchedulingApi.ReadAsync<List<PortalAppointment>>(await bob.GetAsync("/api/portal/appointments"));
        Assert.Equal("Pending", bobs.Single(a => a.Id == bobsVisit.Id).Status);
    }

    [Fact]
    public async Task Assistant_WhenOffOrDown_SaysSo_AndOnlyPatientsCanUseIt()
    {
        var portal = await PortalClinicAsync();
        var alice = Patient(await SignInNewPatientAsync(portal, "Alice"));
        var bob = Patient(await SignInNewPatientAsync(portal, "Bob"));
        try
        {
            ChatModel.Enabled = false;
            Assert.False((await SchedulingApi.ReadAsync<AssistantStatus>(await alice.GetAsync("/api/portal/assistant"))).Enabled);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await PostAsync(alice, null, "Hello")).StatusCode);

            ChatModel.Script(_ => throw new ChatModelException("The model is down."));
            var status = await SchedulingApi.ReadAsync<AssistantStatus>(await alice.GetAsync("/api/portal/assistant"));
            Assert.True(status.Enabled);
            Assert.Equal(1000, status.MaxMessageLength);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await PostAsync(alice, null, "Hello")).StatusCode);

            Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(alice, null, "   ")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(alice, null, new string('a', 1001))).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(portal.Clinic.Admin.Http, null, "Hello")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(Anonymous(), null, "Hello")).StatusCode);

            // A conversation belongs to the patient who started it: anyone else gets a new one.
            ChatModel.Script(_ => Say("Hi Alice"), r =>
            {
                Assert.Single(r.Messages); // none of Alice's messages
                return Say("Hi Bob");
            });
            var alices = await SendAsync(alice, null, "My secret question");
            var bobs = await SendAsync(bob, alices.ConversationId, "Hello");
            Assert.NotEqual(alices.ConversationId, bobs.ConversationId);
        }
        finally
        {
            ChatModel.Enabled = true;
        }
    }

    [Fact]
    public async Task Assistant_OnlyChangesAVisitAfterThePatientSaysYesToThatChange()
    {
        var portal = await PortalClinicAsync(s => s with { MinNoticeHours = 0 });
        var (doctorId, serviceId) = await DoctorAndServiceAsync(portal);
        var patient = Patient(await SignInNewPatientAsync(portal, "Ivy"));
        var slot = (await SlotsAsync(patient, doctorId, serviceId, SchedulingApi.FutureClinicDate(3)))[0];
        var visit = await SchedulingApi.ReadAsync<PortalAppointment>(await BookAsync(patient, doctorId, serviceId, slot.StartTimeUtc));
        var cancel = ("cancel_appointment", (object)new { appointment_id = visit.Id.ToString() });

        static bool NeedsConfirmation(ChatRequest r) =>
            LastResult(r, "cancel_appointment").TryGetProperty("needs_confirmation", out var value) && value.GetBoolean();

        // Asking twice in one turn doesn't count as the patient agreeing.
        ChatModel.Script(
            _ => Call(cancel),
            r =>
            {
                Assert.True(NeedsConfirmation(r));
                Assert.StartsWith("Cancel the ", LastResult(r, "cancel_appointment").GetProperty("change").GetString());
                return Call(cancel);
            },
            r =>
            {
                Assert.True(NeedsConfirmation(r));
                return Say("Shall I cancel it?");
            });
        var asked = await SendAsync(patient, null, "Cancel my visit");
        Assert.False(asked.AppointmentsChanged);

        // A no isn't a yes, even when the model asks for the same change.
        ChatModel.Script(
            _ => Call(cancel),
            r =>
            {
                Assert.True(NeedsConfirmation(r));
                return Say("OK, I'll keep it.");
            });
        var refused = await SendAsync(patient, asked.ConversationId, "No, don't cancel it");
        Assert.False(refused.AppointmentsChanged);
        var visits = await SchedulingApi.ReadAsync<List<PortalAppointment>>(await patient.GetAsync("/api/portal/appointments"));
        Assert.Equal("Pending", visits.Single(a => a.Id == visit.Id).Status);

        // That reply still put the change to the patient, so a yes now goes through.
        ChatModel.Script(
            _ => Call(cancel),
            r =>
            {
                Assert.True(LastResult(r, "cancel_appointment").GetProperty("ok").GetBoolean());
                return Say("It's cancelled.");
            });
        var done = await SendAsync(patient, asked.ConversationId, "Actually yes, go ahead");
        Assert.True(done.AppointmentsChanged);
        visits = await SchedulingApi.ReadAsync<List<PortalAppointment>>(await patient.GetAsync("/api/portal/appointments"));
        Assert.Equal("Cancelled", visits.Single(a => a.Id == visit.Id).Status);
    }

    private static string FirstId(JsonElement result, string list) => result.GetProperty(list)[0].GetProperty("id").GetString()!;

    private static Task<HttpResponseMessage> PostAsync(HttpClient http, Guid? conversationId, string message) =>
        http.PostAsJsonAsync("/api/portal/assistant/messages", new { conversationId, message }, SchedulingApi.Json);

    private static async Task<AssistantReply> SendAsync(HttpClient http, Guid? conversationId, string message) =>
        await SchedulingApi.ReadAsync<AssistantReply>(await PostAsync(http, conversationId, message));
}

public sealed partial class PortalTests
{
    [Fact]
    public async Task Assistant_ThatClaimsAChangeWithoutMakingIt_IsCorrected_OrReplacedWithAnHonestReply()
    {
        var portal = await PortalClinicAsync();
        var patient = Patient(await SignInNewPatientAsync(portal, "Hana"));

        // Claims, is told nothing happened, then answers honestly: the honest answer goes through.
        ChatModel.Script(
            _ => Say("Your appointment has been cancelled."),
            r =>
            {
                Assert.Contains("no tool was called", r.Messages[^1].Text);
                return Say("Sorry, that isn't done yet. Shall I cancel it?");
            });
        var corrected = await SendAsync(patient, null, "Yes, cancel it");
        Assert.Equal("Sorry, that isn't done yet. Shall I cancel it?", corrected.Reply);
        Assert.False(corrected.AppointmentsChanged);

        // The reminder isn't kept in the chat.
        ChatModel.Script(r =>
        {
            Assert.DoesNotContain(r.Messages, m => m.Text?.Contains("no tool was called") == true);
            Assert.DoesNotContain(r.Messages, m => m.Text == "Your appointment has been cancelled.");
            return Say("OK");
        });
        await SendAsync(patient, corrected.ConversationId, "Never mind");

        // Keeps claiming, past both reminders: the patient never sees the false claim.
        ChatModel.Script(
            _ => Say("Your appointment has been cancelled."),
            _ => Say("Done, your appointment has been cancelled."),
            _ => Say("Your appointment has been cancelled, really."));
        var replaced = await SendAsync(patient, null, "Yes, cancel it");
        Assert.DoesNotContain("has been cancelled", replaced.Reply);
        Assert.Contains("nothing has been changed", replaced.Reply);
    }

    [Fact]
    public async Task Assistant_ThatCallsAPendingVisitConfirmed_IsToldToLook()
    {
        var portal = await PortalClinicAsync(s => s with { MinNoticeHours = 0 });
        var (doctorId, serviceId) = await DoctorAndServiceAsync(portal);
        var patient = Patient(await SignInNewPatientAsync(portal, "Jade"));
        var slot = (await SlotsAsync(patient, doctorId, serviceId, SchedulingApi.FutureClinicDate(4)))[0];
        await BookAsync(patient, doctorId, serviceId, slot.StartTimeUtc); // Pending: the clinic doesn't auto-confirm

        ChatModel.Script(
            _ => Say("Yes, your visit is confirmed."),
            r =>
            {
                Assert.Contains("no tool result shows a visit with status Confirmed", r.Messages[^1].Text);
                return Call(("list_my_appointments", new { }));
            },
            r =>
            {
                Assert.StartsWith("Pending", LastResult(r, "list_my_appointments").GetProperty("upcoming")[0].GetProperty("status").GetString());
                return Say("It's booked and waiting for the clinic to confirm it.");
            });
        var reply = await SendAsync(patient, null, "Is my visit confirmed?");
        Assert.Equal("It's booked and waiting for the clinic to confirm it.", reply.Reply);

        // Past the reminders, the patient still never hears "confirmed".
        ChatModel.Script(
            _ => Say("Your visit is confirmed."),
            _ => Say("Yes, it's confirmed."),
            _ => Say("You're all confirmed."));
        var replaced = await SendAsync(patient, null, "Is my visit confirmed?");
        Assert.Contains("whether the clinic has confirmed it", replaced.Reply);
    }
}

public sealed partial class PortalTests
{
    [Fact]
    public async Task Assistant_ThatStalls_IsRemindedOnce_AndToolsAcceptNamesInsteadOfIds()
    {
        var portal = await PortalClinicAsync(s => s with { MinNoticeHours = 0 });
        await DoctorAndServiceAsync(portal);
        var patient = Patient(await SignInNewPatientAsync(portal, "Ines"));
        string? serviceName = null;
        string? checkTime = null;

        ChatModel.Script(
            _ => Say("Let me check the services for you."),
            r =>
            {
                Assert.Contains("don't say you will check", r.Messages[^1].Text);
                return Call(("list_services", new { }));
            },
            r =>
            {
                serviceName = LastResult(r, "list_services").GetProperty("services")[0].GetProperty("name").GetString();
                // A name instead of an id still works.
                return Call(("find_open_times", new
                {
                    service_id = serviceName,
                    date = SchedulingApi.FutureClinicDate().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                }));
            },
            r =>
            {
                var result = LastResult(r, "find_open_times");
                Assert.False(result.TryGetProperty("error", out _));
                var second = result.GetProperty("dentists")[0].GetProperty("times")[1];
                // Each time comes with the words to say it in, e.g. "9:30 AM".
                Assert.EndsWith("M", second.GetProperty("say").GetString());
                checkTime = second.GetProperty("start").GetString()![^5..];
                return Call(("find_open_times", new
                {
                    service_id = serviceName,
                    date = SchedulingApi.FutureClinicDate().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    after = checkTime
                }));
            },
            r =>
            {
                // Asking about one time: told directly whether it's open, and the time echoed back in 12-hour form.
                var result = LastResult(r, "find_open_times");
                Assert.True(result.GetProperty("open_at_exactly_that_time").GetBoolean());
                Assert.Equal(TimeOnly.Parse(checkTime!, CultureInfo.InvariantCulture).ToString("h:mm tt", CultureInfo.GetCultureInfo("en-US")),
                    result.GetProperty("after").GetString());
                return Say("We have times that day.");
            });

        var reply = await SendAsync(patient, null, "What can I book?");
        Assert.Equal("We have times that day.", reply.Reply);
        Assert.NotNull(serviceName);
    }
}
