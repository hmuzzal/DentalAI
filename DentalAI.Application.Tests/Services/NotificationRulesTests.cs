using DentalAI.Application.Common.Notifications;
using DentalAI.Application.Services;
using DentalAI.Domain.Entities;
using DentalAI.Domain.Enums;
using Xunit;

namespace DentalAI.Application.Tests.Services;

public sealed class NotificationRulesTests
{
    private static readonly TimeZoneInfo NewYork = SchedulingEngine.ResolveTimeZone("America/New_York");

    [Theory]
    [InlineData(ContactMethod.Sms, true, "a@b.com", MessageChannel.Sms)]
    [InlineData(ContactMethod.Email, true, "a@b.com", MessageChannel.Email)]
    [InlineData(ContactMethod.Phone, true, "a@b.com", MessageChannel.Sms)]     // a call isn't automated: text if allowed
    [InlineData(ContactMethod.Sms, false, "a@b.com", MessageChannel.Email)]    // no consent: never a text
    [InlineData(ContactMethod.Sms, false, null, null)]
    [InlineData(ContactMethod.Email, false, null, null)]
    public void ChooseChannel_TextsOnlyWithConsent(ContactMethod preferred, bool smsOptIn, string? email, MessageChannel? expected)
    {
        var patient = new Patient { Phone = "+12125550142", Email = email, PreferredContactMethod = preferred, SmsOptIn = smsOptIn };

        Assert.Equal(expected, NotificationRules.ChooseChannel(patient, new ClinicNotificationSettings()));
    }

    [Fact]
    public void ChooseChannel_RespectsClinicSwitches()
    {
        var patient = new Patient { Phone = "+12125550142", Email = "a@b.com", PreferredContactMethod = ContactMethod.Sms, SmsOptIn = true };

        Assert.Equal(MessageChannel.Email, NotificationRules.ChooseChannel(patient, new ClinicNotificationSettings { SmsEnabled = false }));
        Assert.Null(NotificationRules.ChooseChannel(patient, new ClinicNotificationSettings { SmsEnabled = false, EmailEnabled = false }));
    }

    [Theory]
    [InlineData("2026-07-06T23:30:00Z", "2026-07-06T23:30:00Z")] // 7:30 PM EDT: allowed
    [InlineData("2026-07-07T02:00:00Z", "2026-07-07T12:00:00Z")] // 10 PM EDT: wait until 8 AM
    [InlineData("2026-07-07T10:00:00Z", "2026-07-07T12:00:00Z")] // 6 AM EDT: wait until 8 AM
    [InlineData("2026-07-07T12:00:00Z", "2026-07-07T12:00:00Z")] // 8 AM EDT exactly: allowed
    public void QuietHours_WrapPastMidnight(string utc, string expected)
    {
        var result = NotificationRules.NextAllowedUtc(DateTime.Parse(utc).ToUniversalTime(), NewYork, new TimeOnly(21, 0), new TimeOnly(8, 0));
        Assert.Equal(DateTime.Parse(expected).ToUniversalTime(), result);
    }

    [Fact]
    public void QuietHours_DaytimeWindow_AndNone()
    {
        var lunch = DateTime.Parse("2026-07-06T17:30:00Z").ToUniversalTime(); // 1:30 PM EDT
        Assert.Equal(DateTime.Parse("2026-07-06T18:00:00Z").ToUniversalTime(),
            NotificationRules.NextAllowedUtc(lunch, NewYork, new TimeOnly(13, 0), new TimeOnly(14, 0)));
        Assert.Equal(lunch, NotificationRules.NextAllowedUtc(lunch, NewYork, new TimeOnly(9, 0), new TimeOnly(9, 0)));
    }

    [Fact]
    public void DueReminders_OnlyOnceTheirTimeComes_AndOnlyTheLatest()
    {
        var start = new DateTime(2026, 7, 10, 14, 0, 0, DateTimeKind.Utc);
        var bookedEarly = start.AddDays(-7);
        int[] slots = [24, 2];

        Assert.Empty(NotificationRules.DueReminderHours(start, bookedEarly, start.AddHours(-30), slots));
        Assert.Equal([24], NotificationRules.DueReminderHours(start, bookedEarly, start.AddHours(-23), slots));
        Assert.Equal([2], NotificationRules.DueReminderHours(start, bookedEarly, start.AddHours(-1), slots)); // missed 24 h: just 2 h
        Assert.Empty(NotificationRules.DueReminderHours(start, bookedEarly, start.AddMinutes(1), slots));    // already started
    }

    [Fact]
    public void DueReminders_SkipSlotsThatWereAlreadyPastWhenBooked()
    {
        var start = new DateTime(2026, 7, 10, 14, 0, 0, DateTimeKind.Utc);
        var bookedLate = start.AddHours(-10); // the confirmation covered the 24 h reminder

        Assert.Empty(NotificationRules.DueReminderHours(start, bookedLate, start.AddHours(-9), [24, 2]));
        Assert.Equal([2], NotificationRules.DueReminderHours(start, bookedLate, start.AddHours(-2), [24, 2]));
        Assert.Empty(NotificationRules.DueReminderHours(start, bookedLate, start.AddHours(-2), [24, 0]));
    }

    [Fact]
    public void RetryDelays_GrowThenStop()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), NotificationRules.RetryDelay(1));
        Assert.Equal(TimeSpan.FromMinutes(60), NotificationRules.RetryDelay(4));
        Assert.Null(NotificationRules.RetryDelay(5));
    }
}

public sealed class MessageTemplatesTests
{
    private static AppointmentFacts Facts(DateTime start, DateTime now) =>
        new("Brooklyn Smiles", "(718) 555-0142", "100 Court St", "Ava", "Dr. Lopez", "Cleaning", start, now);

    [Theory]
    [InlineData(0, "today at 9:00 AM")]
    [InlineData(1, "tomorrow at 9:00 AM")]
    [InlineData(5, "on Wed, Jul 15 at 9:00 AM")]
    public void When_ReadsNaturally(int daysAhead, string expected)
    {
        var now = new DateTime(2026, 7, 10, 7, 0, 0);
        Assert.Equal(expected, MessageTemplates.When(Facts(now.Date.AddDays(daysAhead).AddHours(9), now)));
    }

    [Theory]
    [InlineData(MessageKind.BookingConfirmation)]
    [InlineData(MessageKind.Reminder)]
    [InlineData(MessageKind.Rescheduled)]
    [InlineData(MessageKind.Cancelled)]
    public void Texts_SayHowToOptOut_AndStayShort(MessageKind kind)
    {
        var now = new DateTime(2026, 7, 10, 7, 0, 0);
        var (subject, body) = MessageTemplates.Appointment(kind, MessageChannel.Sms, Facts(now.AddDays(1), now));

        Assert.Null(subject);
        Assert.EndsWith(MessageTemplates.OptOut, body);
        Assert.StartsWith("Brooklyn Smiles:", body);
        Assert.True(body.Length <= 306, $"{body.Length} chars: more than two SMS segments"); // 2 × 153
    }

    [Theory]
    [InlineData(MessageKind.BookingConfirmation)]
    [InlineData(MessageKind.Reminder)]
    [InlineData(MessageKind.Rescheduled)]
    [InlineData(MessageKind.Cancelled)]
    public void DatesLaterInTheWeek_ReadNaturally(MessageKind kind)
    {
        var now = new DateTime(2026, 7, 10, 7, 0, 0);
        foreach (var channel in new[] { MessageChannel.Sms, MessageChannel.Email })
        {
            var (subject, body) = MessageTemplates.Appointment(kind, channel, Facts(now.AddDays(4), now));
            foreach (var text in new[] { subject ?? string.Empty, body })
            {
                foreach (var clumsy in new[] { " for on ", " to on ", ": on ", " on on " })
                    Assert.DoesNotContain(clumsy, text);
            }
        }
    }

    [Fact]
    public void Emails_HaveASubjectAndTheClinicsPhone()
    {
        var now = new DateTime(2026, 7, 10, 7, 0, 0);
        var (subject, body) = MessageTemplates.Appointment(MessageKind.Reminder, MessageChannel.Email, Facts(now.AddDays(1).AddHours(2), now));

        Assert.Equal("Reminder: Cleaning tomorrow at 9:00 AM", subject);
        Assert.StartsWith("Hi Ava,", body);
        Assert.Contains("Call us at (718) 555-0142", body);
    }
}
