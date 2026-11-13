using System.Globalization;
using System.Text.Json;
using DentalAI.Application.Common.Ai;
using DentalAI.Application.Common.Interfaces;
using DentalAI.Application.Common.Models;
using DentalAI.Application.Common.Phone;
using DentalAI.Application.DTOs.Portal;
using DentalAI.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace DentalAI.Application.Services;

/// <summary>
/// The AI receptionist in the patient portal. It chats with the signed-in patient and acts through tools that call
/// <see cref="PortalService"/>, so it can do exactly what the patient could do on the portal pages, under the same
/// clinic rules (notice, how far ahead, cut-off, limits, double booking), and nothing more. A weaker model makes the
/// assistant less helpful, never less safe. Which model runs is configuration (<see cref="IChatModel"/>).
/// </summary>
public sealed class ReceptionistService(
    PortalService portal,
    IChatModel model,
    IAssistantConversationStore conversations,
    AssistantLimits limits,
    ICurrentUserService currentUser,
    IDateTimeProvider clock,
    ILogger<ReceptionistService> logger)
{
    private const int MaxToolCallsPerRound = 6;
    private const int MaxRemindersPerMessage = 2;
    private const int MaxHistoryMessages = 40;
    private const int SuggestedTimesPerDentist = 4;
    private const int CalendarDays = 14;
    private const string LocalFormat = "yyyy-MM-dd'T'HH:mm";
    private static readonly string[] AcceptedStartFormats = [LocalFormat, "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd HH:mm"];
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public Result<AssistantStatusDto> GetStatus() =>
        Result<AssistantStatusDto>.Ok(new AssistantStatusDto(model.IsEnabled, model.Provider, limits.MaxMessageLength));

    public async Task<Result<AssistantReplyDto>> SendAsync(AssistantMessageRequest request, CancellationToken cancellationToken)
    {
        if (!model.IsEnabled)
            return Result<AssistantReplyDto>.Fail(ResultStatus.Unavailable, "The assistant isn't available.");

        var text = (request.Message ?? string.Empty).Trim();
        if (text.Length == 0)
            return Result<AssistantReplyDto>.Fail(ResultStatus.ValidationError, "Type a message.");
        if (text.Length > limits.MaxMessageLength)
            return Result<AssistantReplyDto>.Fail(ResultStatus.ValidationError, $"Please keep messages under {limits.MaxMessageLength} characters.");

        // Also checks the session and that the portal is still on.
        var profile = await portal.GetProfileAsync(cancellationToken);
        if (!profile.Success) return Result<AssistantReplyDto>.Fail(profile.Status, profile.Message);
        var patientId = currentUser.PatientId!.Value;

        var conversation = (request.ConversationId is { } id ? conversations.Get(patientId, id) : null) ?? new AssistantConversation();
        conversation.Turn++;
        var context = new ToolContext(profile.Data!, SchedulingEngine.ResolveTimeZone(profile.Data!.Clinic.TimeZone), conversation, text);

        // Work on a copy: if the model can't be reached, the chat stays as it was and the patient can send again.
        var messages = new List<ChatMessage>(conversation.Messages) { ChatMessage.User(text) };
        var system = await SystemPromptAsync(context, cancellationToken);
        var changed = false;
        string? reply = null;
        // Reminders added when the model claims a change it didn't make or stalls; removed before the chat is saved.
        var corrections = new List<ChatMessage>();

        void Remind(string? answer, string note)
        {
            ChatMessage[] pair = [ChatMessage.Assistant(answer), ChatMessage.User(note)];
            corrections.AddRange(pair);
            messages.AddRange(pair);
        }

        try
        {
            for (var round = 0; round < limits.MaxModelCallsPerMessage && reply is null; round++)
            {
                var response = await model.CompleteAsync(new ChatRequest(system, TrimHistory(messages), Tools), cancellationToken);
                if (response.ToolCalls.Count == 0)
                {
                    var answer = response.Text?.Trim();
                    var canRemind = corrections.Count < MaxRemindersPerMessage * 2 && round + 1 < limits.MaxModelCallsPerMessage;
                    if (!changed && ClaimsAChange(answer))
                    {
                        // Small models sometimes say "it's cancelled" without calling the tool. Never pass that on:
                        // tell the model that nothing was done, then fall back to an honest answer.
                        if (canRemind)
                        {
                            Remind(answer, ClaimWithoutActionNote);
                            continue;
                        }

                        logger.LogWarning("The {Provider} assistant claimed a change it didn't make; the reply was replaced.", model.Provider);
                        reply = $"Sorry, I couldn't complete that, so nothing has been changed. Please try again, use your Visits page{CallText(context)}.";
                        break;
                    }

                    // "Your visit is confirmed" when no tool result shows a Confirmed visit: online bookings wait for the
                    // clinic, and small models call them confirmed anyway. Make it look, or don't say it.
                    if (SaysConfirmed(answer) && !messages.Any(m => m.Role == ChatRole.Tool && m.Text?.Contains("\"status\":\"Confirmed\"") == true))
                    {
                        if (canRemind)
                        {
                            Remind(answer, ConfirmedWithoutProofNote);
                            continue;
                        }

                        reply = $"Your visit is booked. Your Visits page shows whether the clinic has confirmed it yet{CallText(context)}.";
                        break;
                    }

                    // "Let me check..." with no tool call, or asking the patient for an id: remind the model to use the
                    // tools itself. Past the reminders, the reply goes through as it is (harmless, just unhelpful).
                    if (canRemind && StallsInsteadOfActing(answer))
                    {
                        Remind(answer, StallNote);
                        continue;
                    }

                    reply = string.IsNullOrEmpty(answer) ? "Sorry, I didn't catch that. Could you say it another way?" : answer;
                    break;
                }

                var calls = response.ToolCalls.Take(MaxToolCallsPerRound).ToList();
                messages.Add(ChatMessage.Assistant(response.Text, calls));
                foreach (var call in calls)
                {
                    var (result, didChange) = await RunToolAsync(call, context, cancellationToken);
                    changed |= didChange;
                    // Tool name and outcome only: arguments can hold the patient's own note (Debug level).
                    logger.LogInformation("Assistant tool {Tool}: {Outcome}", call.Name, Outcome(result));
                    logger.LogDebug("Assistant tool {Tool} arguments: {Arguments}", call.Name, call.ArgumentsJson);
                    messages.Add(ChatMessage.ToolResult(call, result));
                }
            }
        }
        catch (ChatModelException ex)
        {
            logger.LogWarning(ex, "The {Provider} assistant model failed: {Reason}", model.Provider, ex.Message);
            if (!changed)
                return Result<AssistantReplyDto>.Fail(ResultStatus.Unavailable,
                    $"The assistant isn't available right now. Please try again in a moment, use the Book page{CallText(context)}.");

            // Something was already booked, moved or cancelled: keep the chat and say so.
            reply = "I've made that change. Please check your visits to see it.";
        }

        reply ??= $"Sorry, I couldn't finish that. You can use the Book page{CallText(context)}.";
        messages.RemoveAll(m => corrections.Any(c => ReferenceEquals(c, m)));
        messages.Add(ChatMessage.Assistant(reply));

        conversation.Messages.Clear();
        conversation.Messages.AddRange(TrimHistory(messages));
        conversations.Save(patientId, conversation);
        return Result<AssistantReplyDto>.Ok(new AssistantReplyDto(conversation.Id, reply, changed));
    }

    // ---- Prompt ----

    /// <summary>
    /// The instructions, with the clinic's services and dentists and their ids included: small models then don't have to
    /// look them up (or guess an id) before finding times. The lists are short.
    /// </summary>
    private async Task<string> SystemPromptAsync(ToolContext context, CancellationToken cancellationToken)
    {
        var clinic = context.Profile.Clinic;
        var services = await portal.ListServicesAsync(cancellationToken);
        var dentists = await portal.ListDoctorsAsync(cancellationToken);
        var serviceLines = services.Success && services.Data!.Count > 0
            ? string.Join("; ", services.Data!.Select(s => $"{context.Conversation.RefFor('S', s.Id)} = {s.Name} ({s.DurationMinutes} min)"))
            : "none can be booked online";
        var dentistLines = dentists.Success && dentists.Data!.Count > 0
            ? string.Join("; ", dentists.Data!.Select(d => $"{context.Conversation.RefFor('D', d.Id)} = {d.Name}"))
            : "none listed";
        var now = TimeZoneInfo.ConvertTimeFromUtc(clock.UtcNow, context.Zone);
        var phone = string.IsNullOrWhiteSpace(clinic.Phone) ? "the clinic" : PhoneNumberRules.Display(clinic.Phone);
        var days = string.Join("; ", Enumerable.Range(0, CalendarDays).Select(offset =>
        {
            var day = now.Date.AddDays(offset);
            var label = offset switch { 0 => " (today)", 1 => " (tomorrow)", _ => string.Empty };
            return $"{day.ToString("ddd MMM d", English)} = {day.ToString("yyyy-MM-dd", English)}{label}";
        }));

        return $"""
            You are the online receptionist for {clinic.Name}, a dental clinic. You are chatting in the clinic's patient portal with {context.Profile.FirstName}, a signed-in patient.
            Right now it is {now.ToString("dddd, MMMM d, yyyy 'at' h:mm tt", English)} at the clinic. Every time you see or give is clinic local time.
            The next two weeks (day = date to pass to tools): {days}.

            You can: answer questions about the clinic, list services and dentists, find open times, and book, move or cancel this patient's own appointments, and show their invoices.

            Rules:
            - Use the tools for every fact about services, dentists, open times, appointments and invoices. Never make up times, availability or amounts.
            - Ids such as S1, D2, A3 come from tool results. Pass them back exactly, but never show them to the patient. Pass start times exactly as find_open_times gave them.
            - To turn a day the patient names ("Friday", "next Monday", "tomorrow") into a date, read it from the two-week list above. Never work it out yourself. Say the day and date back, e.g. "Fri, Nov 20".
            - When offering times, offer the three or four that find_open_times returns, never a long list. If the patient wants a different time, call find_open_times again with after set to that time.
            - Never say you will check or look something up: call the tool right away, in the same turn, and answer with what it returns.
            - Never ask the patient for an id. To find their appointment, call list_my_appointments.
            - For "morning" use times before 12:00, for "afternoon" pass after="12:00" to find_open_times. after is 24-hour time: 1 PM = 13:00, 2:15 PM = 14:15, 4:30 PM = 16:30. A time the patient gives without AM or PM between 1 and 6 means PM.
            - To check a time the patient asks for, pass it as after and read open_at_exactly_that_time. When telling the patient a time, use its "say" value.
            - book_appointment, reschedule_appointment and cancel_appointment first answer needs_confirmation with the exact change. Tell the patient that change and ask them to confirm, even if they already picked the time. Only after they say yes, call the same tool again with the same values. Never say it is done until a tool result says ok.
            - A visit whose status is Pending is waiting for the clinic to confirm it. Say so; never call it confirmed unless its status is Confirmed.
            - If a day has no open times, offer to check the next few days.
            - Online bookings need {clinic.MinNoticeHours} hours' notice and can be up to {clinic.MaxDaysAhead} days ahead. Visits can be moved or cancelled online until {clinic.CancelCutoffHours} hours before.
            - You can't give medical advice. For severe pain, swelling or bleeding, tell them to call {phone}; in an emergency, call local emergency services.
            - You can only see and change this patient's records. For prices, insurance, or anything you can't do, suggest calling {phone}.
            - Reply in plain text, in 1 to 3 short, friendly sentences. Write times like "Tue, Nov 18 at 3:00 PM".

            Clinic: {clinic.Name}. Phone: {phone}. Address: {clinic.Address ?? "not listed"}.
            Services you can book (id = name): {serviceLines}.
            Dentists (id = name): {dentistLines}.
            """;
    }

    private const string ClaimWithoutActionNote =
        "[Note from the system, not the patient: your last reply said something was booked, moved or cancelled, but no tool was " +
        "called, so nothing has changed. If the patient has clearly said yes, call the tool now. Otherwise tell them it is not done yet.]";

    private static readonly System.Text.RegularExpressions.Regex ChangeClaim = new(
        @"\b(?:(?:has|have) been|is now|are now|successfully|i've|i have|we've|we have)\s+(?:been\s+)?(?:booked|cancell?ed|rescheduled|moved)\b|\byou're (?:all )?(?:booked|set)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>Does the reply say a visit was booked, moved or cancelled ("has been cancelled", "you're booked")?</summary>
    public static bool ClaimsAChange(string? reply) => !string.IsNullOrEmpty(reply) && ChangeClaim.IsMatch(reply);

    private const string ConfirmedWithoutProofNote =
        "[Note from the system, not the patient: your last reply said a visit is confirmed, but no tool result shows a visit with " +
        "status Confirmed. Online bookings are Pending until the clinic confirms them. Call list_my_appointments and tell the " +
        "patient the status it shows; say Pending visits are waiting for the clinic to confirm.]";

    private static readonly System.Text.RegularExpressions.Regex ConfirmedClaim = new(
        @"\b(?:is|are|has been|have been|it's|you're|you are)\s+(?:now\s+|all\s+)?confirmed\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static readonly System.Text.RegularExpressions.Regex ConfirmedHedge = new(
        @"\b(?:waiting|pending|once|until|not yet|not been|isn't|hasn't)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>Does the reply say a visit is confirmed ("your visit is confirmed"), without saying it's still waiting?</summary>
    public static bool SaysConfirmed(string? reply) =>
        !string.IsNullOrEmpty(reply) && ConfirmedClaim.IsMatch(reply) && !ConfirmedHedge.IsMatch(reply);

    private const string StallNote =
        "[Note from the system, not the patient: don't say you will check, list or find something, and never ask the patient for " +
        "an id. Call the right tool now (list_my_appointments, list_services, list_dentists, find_open_times) and answer with what it returns.]";

    private static readonly System.Text.RegularExpressions.Regex Stall = new(
        @"\b(?:let(?:'s| me| us)|i'll|i will|i am going to|i'm going to)\s+(?:now\s+)?(?:check|list|find|look|search|get|fetch|see)\b|\bone moment\b|\bplease (?:wait|hold)\b|\b(?:provide|give|reply with|tell me|share)\b[^.?!]*\bid\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>Does the reply promise to look something up ("Let me check...") or ask the patient for an id, instead of acting?</summary>
    public static bool StallsInsteadOfActing(string? reply) => !string.IsNullOrEmpty(reply) && Stall.IsMatch(reply);

    private static string CallText(ToolContext context) =>
        string.IsNullOrWhiteSpace(context.Profile.Clinic.Phone)
            ? ", or call the clinic"
            : $", or call {PhoneNumberRules.Display(context.Profile.Clinic.Phone)}";

    /// <summary>The most recent messages, starting at a patient message so no tool result loses its call.</summary>
    private static List<ChatMessage> TrimHistory(List<ChatMessage> messages)
    {
        if (messages.Count <= MaxHistoryMessages) return messages;
        var start = messages.Count - MaxHistoryMessages;
        while (start < messages.Count - 1 && messages[start].Role != ChatRole.User) start++;
        return messages.GetRange(start, messages.Count - start);
    }

    // ---- Tools ----

    private static readonly IReadOnlyList<ChatTool> Tools =
    [
        new("list_services", "Lists the services that can be booked online, with their ids and lengths.",
            """{"type":"object","properties":{}}"""),
        new("list_dentists", "Lists the clinic's dentists, with their ids.",
            """{"type":"object","properties":{}}"""),
        new("find_open_times",
            "Finds open appointment times on one day for a service, for one dentist or for every dentist. Returns up to four start times across the day per dentist, and how many others are open; pass after=HH:mm for a specific time.",
            """
            {"type":"object","properties":{
              "service_id":{"type":"string","description":"Service id from list_services, e.g. S1."},
              "date":{"type":"string","description":"Clinic-local day, YYYY-MM-DD."},
              "dentist_id":{"type":"string","description":"Optional dentist id from list_dentists, e.g. D1. Leave out to check every dentist."},
              "appointment_id":{"type":"string","description":"Optional: when moving an appointment, its id (e.g. A1), so its own time counts as free."},
              "after":{"type":"string","description":"Optional: only times at or after this clinic-local time, HH:mm (e.g. 12:00 for the afternoon)."}
            },"required":["service_id","date"]}
            """),
        new("list_my_appointments", "Lists this patient's upcoming and recent appointments, with ids and whether each can still be changed online.",
            """{"type":"object","properties":{}}"""),
        new("book_appointment", "Books an appointment for this patient. The first call only checks it and returns needs_confirmation; call again with the same values after the patient says yes.",
            """
            {"type":"object","properties":{
              "service_id":{"type":"string","description":"Service id, e.g. S1."},
              "dentist_id":{"type":"string","description":"Dentist id, e.g. D1."},
              "start":{"type":"string","description":"Start time exactly as find_open_times gave it, YYYY-MM-DDTHH:mm (clinic local)."},
              "note":{"type":"string","description":"Optional short note for the clinic from the patient."}
            },"required":["service_id","dentist_id","start"]}
            """),
        new("reschedule_appointment", "Moves one of this patient's appointments to a new time with the same dentist. The first call only checks it and returns needs_confirmation; call again with the same values after the patient says yes.",
            """
            {"type":"object","properties":{
              "appointment_id":{"type":"string","description":"Appointment id from list_my_appointments, e.g. A1."},
              "start":{"type":"string","description":"New start time exactly as find_open_times gave it, YYYY-MM-DDTHH:mm."}
            },"required":["appointment_id","start"]}
            """),
        new("cancel_appointment", "Cancels one of this patient's appointments. The first call only checks it and returns needs_confirmation; call again with the same values after the patient says yes.",
            """
            {"type":"object","properties":{
              "appointment_id":{"type":"string","description":"Appointment id from list_my_appointments, e.g. A1."}
            },"required":["appointment_id"]}
            """),
        new("list_my_invoices", "Lists this patient's invoices and what they still owe.",
            """{"type":"object","properties":{}}""")
    ];

    private async Task<(string Json, bool Changed)> RunToolAsync(ChatToolCall call, ToolContext context, CancellationToken cancellationToken)
    {
        JsonElement args;
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson);
            args = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return (Error("The arguments were not valid JSON."), false);
        }

        if (args.ValueKind != JsonValueKind.Object) return (Error("The arguments must be a JSON object."), false);

        try
        {
            return call.Name switch
            {
                "list_services" => (await ListServicesAsync(context, cancellationToken), false),
                "list_dentists" => (await ListDentistsAsync(context, cancellationToken), false),
                "find_open_times" => (await FindOpenTimesAsync(args, context, cancellationToken), false),
                "list_my_appointments" => (await ListAppointmentsAsync(context, cancellationToken), false),
                "book_appointment" => await BookAsync(args, context, cancellationToken),
                "reschedule_appointment" => await RescheduleAsync(args, context, cancellationToken),
                "cancel_appointment" => await CancelAsync(args, context, cancellationToken),
                "list_my_invoices" => (await ListInvoicesAsync(cancellationToken), false),
                _ => (Error($"There is no tool called \"{call.Name}\"."), false)
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Assistant tool {Tool} failed.", call.Name);
            return (Error("Something went wrong on our side. Suggest the patient tries the portal pages or calls the clinic."), false);
        }
    }

    private async Task<string> ListServicesAsync(ToolContext context, CancellationToken cancellationToken)
    {
        var result = await portal.ListServicesAsync(cancellationToken);
        if (!result.Success) return Error(result.Message);
        return Json(new
        {
            Services = result.Data!.Select(s => new
            {
                Id = context.Conversation.RefFor('S', s.Id),
                s.Name,
                s.Description,
                Minutes = s.DurationMinutes
            })
        });
    }

    private async Task<string> ListDentistsAsync(ToolContext context, CancellationToken cancellationToken)
    {
        var result = await portal.ListDoctorsAsync(cancellationToken);
        if (!result.Success) return Error(result.Message);
        return Json(new
        {
            Dentists = result.Data!.Select(d => new { Id = context.Conversation.RefFor('D', d.Id), d.Name, d.Specialty })
        });
    }

    private async Task<string> FindOpenTimesAsync(JsonElement args, ToolContext context, CancellationToken cancellationToken)
    {
        if (await ResolveServiceAsync(Text(args, "service_id"), context, cancellationToken) is not { } serviceId)
            return Error("Unknown service_id. Call list_services and use an id from it.");
        if (!DateOnly.TryParseExact(Text(args, "date"), "yyyy-MM-dd", English, DateTimeStyles.None, out var date))
            return Error("date must be YYYY-MM-DD.");

        TimeOnly? after = null;
        if (Text(args, "after") is { Length: > 0 } afterText)
        {
            if (!TimeOnly.TryParseExact(afterText, ["HH:mm", "H:mm"], English, DateTimeStyles.None, out var parsed))
                return Error("after must be HH:mm, e.g. 12:00.");
            after = parsed;
        }

        Guid? appointmentId = null;
        if (Text(args, "appointment_id") is { Length: > 0 } appointmentText)
        {
            appointmentId = context.Conversation.Resolve('A', appointmentText);
            if (appointmentId is null) return Error("Unknown appointment_id. Call list_my_appointments and use an id from it.");
        }

        var doctors = await portal.ListDoctorsAsync(cancellationToken);
        if (!doctors.Success) return Error(doctors.Message);
        var candidates = doctors.Data!.ToList();
        if (Text(args, "dentist_id") is { Length: > 0 } dentistText)
        {
            var dentistId = await ResolveDentistAsync(dentistText, context, cancellationToken);
            candidates = candidates.Where(d => d.Id == dentistId).ToList();
            if (candidates.Count == 0) return Error("Unknown dentist_id. Call list_dentists and use an id from it.");
        }

        var dentists = new List<object>();
        var openCount = 0;
        var moreTimes = false;
        var openAtAfter = false;
        foreach (var doctor in candidates)
        {
            var slots = await portal.GetSlotsAsync(doctor.Id, serviceId, date, appointmentId, cancellationToken);
            if (!slots.Success)
            {
                if (slots.Status is ResultStatus.NotFound) return Error(slots.Message);
                continue; // e.g. this dentist doesn't offer the service
            }

            var starts = slots.Data!
                .Select(s => Local(s.StartTimeUtc, context))
                .Where(local => after is null || TimeOnly.FromDateTime(local) >= after)
                .ToList();
            openCount += starts.Count;
            moreTimes |= starts.Count > SuggestedTimesPerDentist;
            openAtAfter |= after is { } wanted && starts.Any(local => TimeOnly.FromDateTime(local) == wanted);
            dentists.Add(new
            {
                Id = context.Conversation.RefFor('D', doctor.Id),
                doctor.Name,
                // A few times across the day: small models read out every time they are given. Each time is also
                // written the way the patient says it, so the model doesn't convert 24-hour times itself.
                Times = SpreadTimes(starts, SuggestedTimesPerDentist)
                    .Select(local => new { Start = local.ToString(LocalFormat, English), Say = local.ToString("h:mm tt", English) }),
                OtherOpenTimes = starts.Count > SuggestedTimesPerDentist ? starts.Count - SuggestedTimesPerDentist : (int?)null
            });
        }

        return Json(new
        {
            Date = date.ToString("yyyy-MM-dd", English),
            Day = date.ToString("dddd, MMM d", English),
            // Echoed back in 12-hour form, so a wrong conversion ("2:15 PM" passed as 13:15) shows up.
            After = after?.ToString("h:mm tt", English),
            OpenAtExactlyThatTime = after is null ? (bool?)null : openAtAfter,
            Dentists = dentists,
            Note = openCount == 0
                ? "No open times that day."
                : moreTimes
                    ? "Offer these times. For a different time, call find_open_times again with after set to it (24-hour HH:mm)."
                    : null
        });
    }

    /// <summary>
    /// Up to <paramref name="count"/> times spread across the list, always starting with the first, so the patient sees
    /// the earliest time and a choice later in the day. With after=HH:mm the first is the one they asked for.
    /// </summary>
    public static IReadOnlyList<T> SpreadTimes<T>(IReadOnlyList<T> times, int count)
    {
        if (times.Count <= count) return times;
        if (count <= 1) return [times[0]];
        return Enumerable.Range(0, count)
            .Select(i => times[(int)Math.Round(i * (times.Count - 1) / (double)(count - 1))])
            .Distinct()
            .ToList();
    }

    private async Task<string> ListAppointmentsAsync(ToolContext context, CancellationToken cancellationToken)
    {
        var result = await portal.ListAppointmentsAsync(cancellationToken);
        if (!result.Success) return Error(result.Message);

        var now = clock.UtcNow;
        var all = result.Data!;
        var upcoming = all.Where(a => a.EndTime > now && a.Status is AppointmentStatus.Pending or AppointmentStatus.Confirmed)
            .OrderBy(a => a.StartTime).Take(10);
        var recent = all.Where(a => !(a.EndTime > now && a.Status is AppointmentStatus.Pending or AppointmentStatus.Confirmed))
            .OrderByDescending(a => a.StartTime).Take(5);
        return Json(new
        {
            Upcoming = upcoming.Select(a => Describe(a, context)),
            Recent = recent.Select(a => Describe(a, context))
        });
    }

    private async Task<(string, bool)> BookAsync(JsonElement args, ToolContext context, CancellationToken cancellationToken)
    {
        if (await ResolveServiceAsync(Text(args, "service_id"), context, cancellationToken) is not { } serviceId)
            return (Error("Unknown service_id. Call list_services and use an id from it."), false);
        if (await ResolveDentistAsync(Text(args, "dentist_id"), context, cancellationToken) is not { } dentistId)
            return (Error("Unknown dentist_id. Call list_dentists and use an id from it."), false);
        if (ReadStart(args, context, out var startUtc) is { } startError) return (Error(startError), false);

        var request = new PortalBookRequest
        {
            ServiceId = serviceId,
            DoctorId = dentistId,
            StartTime = startUtc,
            Note = Text(args, "note")
        };
        var key = $"book|{serviceId:N}|{dentistId:N}|{startUtc:O}";
        if (!Confirmed(context, key))
        {
            var preview = await portal.PreviewBookingAsync(request, cancellationToken);
            return preview.Success
                ? (AskToConfirm(context, key, $"Book {preview.Data!.ServiceName} with {preview.Data.DoctorName} on {When(preview.Data.StartTimeUtc, context)}"), false)
                : (Error(preview.Message), false);
        }

        var result = await portal.BookAsync(request, cancellationToken);
        return result.Success
            ? (Json(new { Ok = true, result.Message, Appointment = Describe(result.Data!, context) }), true)
            : (Error(result.Message), false);
    }

    private async Task<(string, bool)> RescheduleAsync(JsonElement args, ToolContext context, CancellationToken cancellationToken)
    {
        if (context.Conversation.Resolve('A', Text(args, "appointment_id")) is not { } appointmentId)
            return (Error("Unknown appointment_id. Call list_my_appointments and use an id from it."), false);
        if (ReadStart(args, context, out var startUtc) is { } startError) return (Error(startError), false);

        var request = new PortalRescheduleRequest { StartTime = startUtc };
        var key = $"move|{appointmentId:N}|{startUtc:O}";
        if (!Confirmed(context, key))
        {
            var preview = await portal.PreviewRescheduleAsync(appointmentId, request, cancellationToken);
            return preview.Success
                ? (AskToConfirm(context, key, $"Move the {preview.Data!.ServiceName} with {preview.Data.DoctorName} from " +
                    $"{When(preview.Data.StartTimeUtc, context)} to {When(preview.Data.NewStartTimeUtc!.Value, context)}"), false)
                : (Error(preview.Message), false);
        }

        var result = await portal.RescheduleAsync(appointmentId, request, cancellationToken);
        return result.Success
            ? (Json(new { Ok = true, result.Message, Appointment = Describe(result.Data!, context) }), true)
            : (Error(result.Message), false);
    }

    private async Task<(string, bool)> CancelAsync(JsonElement args, ToolContext context, CancellationToken cancellationToken)
    {
        if (context.Conversation.Resolve('A', Text(args, "appointment_id")) is not { } appointmentId)
            return (Error("Unknown appointment_id. Call list_my_appointments and use an id from it."), false);

        var key = $"cancel|{appointmentId:N}";
        if (!Confirmed(context, key))
        {
            var preview = await portal.PreviewCancelAsync(appointmentId, cancellationToken);
            return preview.Success
                ? (AskToConfirm(context, key, $"Cancel the {preview.Data!.ServiceName} with {preview.Data.DoctorName} on {When(preview.Data.StartTimeUtc, context)}"), false)
                : (Error(preview.Message), false);
        }

        var result = await portal.CancelAsync(appointmentId, cancellationToken);
        return result.Success
            ? (Json(new { Ok = true, result.Message, Appointment = Describe(result.Data!, context) }), true)
            : (Error(result.Message), false);
    }

    private async Task<string> ListInvoicesAsync(CancellationToken cancellationToken)
    {
        var result = await portal.ListInvoicesAsync(1, 10, cancellationToken);
        if (!result.Success) return Error(result.Message);
        var data = result.Data!;
        return Json(new
        {
            Invoices = data.Items.Select(i => new
            {
                i.Number,
                Status = i.Status.ToString(),
                Issued = i.IssueDate?.ToString("yyyy-MM-dd", English),
                Due = i.DueDate?.ToString("yyyy-MM-dd", English),
                Overdue = i.IsOverdue,
                i.Currency,
                i.Total,
                Paid = i.AmountPaid,
                i.Balance
            }),
            Outstanding = data.Outstanding.Select(o => new { o.Currency, o.Amount }),
            TotalInvoices = data.TotalCount
        });
    }

    // ---- Confirmation ----
    // Asking the patient before a change isn't left to the model. The first call for a change only checks it and stores
    // it as pending. It runs only when the model asks for exactly the same change while answering the patient's very next
    // message, and that message says yes. A patient picking a time ("the first one") therefore gets asked, not booked.

    private static bool Confirmed(ToolContext context, string key)
    {
        var conversation = context.Conversation;
        var confirmed = conversation.Pending is { } pending
            && pending.Key == key
            && pending.Turn == conversation.Turn - 1
            && SaysYes(context.PatientMessage);
        if (confirmed) conversation.Pending = null;
        return confirmed;
    }

    private static string AskToConfirm(ToolContext context, string key, string change)
    {
        context.Conversation.Pending = new PendingChange(key, context.Conversation.Turn);
        return Json(new
        {
            Ok = false,
            NeedsConfirmation = true,
            Change = change,
            Instruction = "Nothing has been changed yet. Ask the patient to confirm exactly this change. Once they say yes, " +
                          "call this tool again with the same values."
        });
    }

    private static readonly System.Text.RegularExpressions.Regex Yes = new(
        @"\b(?:y|yes|yeah|yea|yep|yup|sure|ok|okay|k|confirm(?:ed)?|correct|right|please do|go ahead|do it|book it|cancel it|move it|sounds good|perfect|great|absolutely|definitely|that works|works for me)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static readonly System.Text.RegularExpressions.Regex No = new(
        @"^\W*(?:no|nope|nah|wait|hold on|stop)\b|\b(?:not|don't|dont|do not|never|instead|rather)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>Does the patient's message say yes ("Yes please", "ok go ahead"), without a no or a change of mind?</summary>
    public static bool SaysYes(string? message) =>
        !string.IsNullOrWhiteSpace(message) && Yes.IsMatch(message) && !No.IsMatch(message);

    private static string When(DateTime utc, ToolContext context) =>
        Local(utc, context).ToString("ddd, MMM d 'at' h:mm tt", English);

    // ---- Helpers ----

    /// <param name="PatientMessage">What the patient wrote this turn.</param>
    private sealed record ToolContext(PortalProfileDto Profile, TimeZoneInfo Zone, AssistantConversation Conversation, string PatientMessage);

    private object Describe(PortalAppointmentDto a, ToolContext context) => new
    {
        Id = context.Conversation.RefFor('A', a.Id),
        Service = a.ServiceName,
        ServiceId = context.Conversation.RefFor('S', a.ServiceId),
        Dentist = a.DoctorName,
        DentistId = context.Conversation.RefFor('D', a.DoctorId),
        Start = Local(a.StartTime, context).ToString(LocalFormat, English),
        When = Local(a.StartTime, context).ToString("ddd, MMM d 'at' h:mm tt", English),
        Status = a.Status == AppointmentStatus.Pending ? "Pending (waiting for the clinic to confirm)" : a.Status.ToString(),
        a.CanCancel,
        a.CanReschedule
    };

    /// <summary>A service id (S1 or a raw id), or, because small models often pass the name instead, a single name match.</summary>
    private async Task<Guid?> ResolveServiceAsync(string? value, ToolContext context, CancellationToken cancellationToken)
    {
        if (context.Conversation.Resolve('S', value) is { } id) return id;
        if (string.IsNullOrWhiteSpace(value)) return null;
        var list = await portal.ListServicesAsync(cancellationToken);
        return list.Success ? SingleMatch(list.Data!.Select(s => (s.Id, s.Name)), value, context, 'S') : null;
    }

    /// <summary>A dentist id (D1 or a raw id), or a single name match ("Dr. Ahmed", "Sarah").</summary>
    private async Task<Guid?> ResolveDentistAsync(string? value, ToolContext context, CancellationToken cancellationToken)
    {
        if (context.Conversation.Resolve('D', value) is { } id) return id;
        if (string.IsNullOrWhiteSpace(value)) return null;
        var list = await portal.ListDoctorsAsync(cancellationToken);
        return list.Success ? SingleMatch(list.Data!.Select(d => (d.Id, d.Name)), value, context, 'D') : null;
    }

    /// <summary>The one item whose name contains every word of <paramref name="value"/> (ignoring "Dr."), if exactly one does.</summary>
    private static Guid? SingleMatch(IEnumerable<(Guid Id, string Name)> items, string value, ToolContext context, char prefix)
    {
        var wanted = Words(value);
        if (wanted.Count == 0) return null;
        var matches = items.Where(item => wanted.All(Words(item.Name).Contains)).ToList();
        if (matches.Count != 1) return null;
        context.Conversation.RefFor(prefix, matches[0].Id);
        return matches[0].Id;

        static HashSet<string> Words(string text) =>
            text.Split([' ', '.', ',', '-', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.ToLowerInvariant())
                .Where(w => w is not ("dr" or "doctor"))
                .ToHashSet();
    }

    private static DateTime Local(DateTime utc, ToolContext context) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), context.Zone);

    /// <summary>A clinic-local start ("2026-11-18T15:00") as UTC; an error message when it can't be read.</summary>
    private static string? ReadStart(JsonElement args, ToolContext context, out DateTime startUtc)
    {
        startUtc = default;
        if (!DateTime.TryParseExact(Text(args, "start"), AcceptedStartFormats, English, DateTimeStyles.None, out var local))
            return "start must be YYYY-MM-DDTHH:mm in clinic time, exactly as find_open_times gave it.";
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (context.Zone.IsInvalidTime(local)) return "That time doesn't exist on that day (clocks change). Choose another.";
        startUtc = TimeZoneInfo.ConvertTimeToUtc(local, context.Zone);
        return null;
    }

    private static string? Text(JsonElement args, string name) =>
        args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;

    private static string Json(object value) => JsonSerializer.Serialize(value, JsonOptions);

    /// <summary>"ok", or the error the tool gave the model (our own wording, no patient data).</summary>
    private static string Outcome(string resultJson)
    {
        using var document = JsonDocument.Parse(resultJson);
        if (document.RootElement.TryGetProperty("error", out var error)) return $"error: {error.GetString()}";
        return document.RootElement.TryGetProperty("needs_confirmation", out _) ? "waiting for the patient to confirm" : "ok";
    }

    private static string Error(string message) => Json(new { Ok = false, Error = message });
}
