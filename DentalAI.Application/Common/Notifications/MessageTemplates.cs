using DentalAI.Domain.Entities;

namespace DentalAI.Application.Common.Notifications;

/// <summary>What an appointment message needs, with the time already in the clinic's zone.</summary>
public sealed record AppointmentFacts(
    string ClinicName,
    string? ClinicPhone,
    string? ClinicAddress,
    string PatientFirstName,
    string DoctorName,
    string ServiceName,
    DateTime LocalStart,
    DateTime LocalNow);

/// <summary>
/// The wording of each message. Texts stay short (one or two SMS segments) and always say how to opt out.
/// English for now; these become translatable with the i18n work.
/// </summary>
public static class MessageTemplates
{
    public const string OptOut = "Reply STOP to opt out.";

    public static (string? Subject, string Body) Appointment(MessageKind kind, MessageChannel channel, AppointmentFacts f)
    {
        var when = When(f);
        return (kind, channel) switch
        {
            (MessageKind.BookingConfirmation, MessageChannel.Sms) =>
                (null, $"{f.ClinicName}: your {f.ServiceName} with {f.DoctorName} is booked {when}. Reply C to confirm. {OptOut}"),
            (MessageKind.Reminder, MessageChannel.Sms) =>
                (null, $"{f.ClinicName}: reminder of your {f.ServiceName} with {f.DoctorName} {when}. Reply C to confirm. {OptOut}"),
            (MessageKind.Rescheduled, MessageChannel.Sms) =>
                (null, $"{f.ClinicName}: your appointment with {f.DoctorName} is now {when}. Reply C to confirm. {OptOut}"),
            (MessageKind.Cancelled, MessageChannel.Sms) =>
                (null, $"{f.ClinicName}: your appointment {when} has been cancelled.{CallToBook(f)} {OptOut}"),

            (MessageKind.BookingConfirmation, _) =>
                ($"Your appointment at {f.ClinicName} {when}", Email(f, $"your {f.ServiceName} with {f.DoctorName} is booked {when}.")),
            (MessageKind.Reminder, _) =>
                ($"Reminder: {f.ServiceName} {when}", Email(f, $"this is a reminder of your {f.ServiceName} with {f.DoctorName} {when}.")),
            (MessageKind.Rescheduled, _) =>
                ($"Your appointment is now {when}", Email(f, $"your appointment with {f.DoctorName} is now {when}.")),
            (MessageKind.Cancelled, _) =>
                ($"Your appointment {when} is cancelled", Email(f, $"your appointment {when} has been cancelled.{CallToBook(f)}")),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not an appointment message.")
        };
    }

    public static (string Subject, string Body) StaffInvite(string firstName, string clinicName, string role, string link, DateTime expiresLocal) =>
        ($"You're invited to {clinicName} on DentalAI",
         $"Hi {firstName},\n\nYou've been invited to join {clinicName} on DentalAI as {role}.\n\n" +
         $"Set your password here (the link works once and expires {expiresLocal:MMM d, h:mm tt}):\n{link}\n\n" +
         "If you weren't expecting this, you can ignore this email.");

    public static string Redacted(string body, string secret) => body.Replace(secret, "[link removed after sending]");

    /// <summary>"today at 9:00 AM", "tomorrow at 9:00 AM" or "on Tue, Nov 10 at 9:00 AM".</summary>
    public static string When(AppointmentFacts f)
    {
        var day = (f.LocalStart.Date - f.LocalNow.Date).Days switch
        {
            0 => "today",
            1 => "tomorrow",
            _ => $"on {f.LocalStart:ddd, MMM d}"
        };
        return $"{day} at {f.LocalStart:h:mm tt}";
    }

    private static string CallToBook(AppointmentFacts f) =>
        string.IsNullOrWhiteSpace(f.ClinicPhone) ? string.Empty : $" Call {f.ClinicPhone} to book a new time.";

    private static string Email(AppointmentFacts f, string sentence) =>
        $"Hi {f.PatientFirstName},\n\n{char.ToUpperInvariant(sentence[0])}{sentence[1..]}\n\n" +
        $"{f.ClinicName}" +
        (string.IsNullOrWhiteSpace(f.ClinicAddress) ? "" : $"\n{f.ClinicAddress}") +
        (string.IsNullOrWhiteSpace(f.ClinicPhone) ? "" : $"\n{f.ClinicPhone}") +
        (string.IsNullOrWhiteSpace(f.ClinicPhone) ? "\n\nNeed to change it? Contact the clinic." : $"\n\nNeed to change it? Call us at {f.ClinicPhone}.") +
        "\n\nThis mailbox isn't monitored, so please don't reply to this email.";
}
