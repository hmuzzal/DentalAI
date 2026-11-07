using DentalAI.Domain.Common;

namespace DentalAI.Domain.Entities;

public enum MessageChannel
{
    Email = 0,
    Sms = 1
}

public enum MessageKind
{
    BookingConfirmation = 0,
    Reminder = 1,
    Rescheduled = 2,
    Cancelled = 3,
    StaffInvite = 4,
    Test = 5,
    /// <summary>An automatic answer to a patient's text (e.g. "Thanks, you're confirmed").</summary>
    Reply = 6
}

public enum MessageStatus
{
    /// <summary>Waiting to be sent (at or after <see cref="OutboundMessage.NotBeforeUtc"/>).</summary>
    Pending = 0,
    /// <summary>Claimed by the sender; back to Pending if it isn't finished within a few minutes.</summary>
    Sending = 1,
    Sent = 2,
    /// <summary>Gave up after retries, or the provider refused it permanently.</summary>
    Failed = 3,
    /// <summary>Not sent on purpose, e.g. the reminder time fell inside quiet hours and after the appointment.</summary>
    Skipped = 4,
    /// <summary>Withdrawn before sending, e.g. the appointment was cancelled.</summary>
    Cancelled = 5
}

/// <summary>
/// A clinic's reminder rules (one row per clinic), managed by its admins. Created on first use with defaults.
/// </summary>
public sealed class ClinicNotificationSettings : BaseEntity, ITenantEntity
{
    public Guid? ClinicId { get; set; }
    public bool EmailEnabled { get; set; } = true;
    /// <summary>Texts go only to patients who agreed to receive them.</summary>
    public bool SmsEnabled { get; set; } = true;
    public bool ConfirmationsEnabled { get; set; } = true;
    public bool RemindersEnabled { get; set; } = true;
    /// <summary>Hours before the appointment for the first reminder (0 = off).</summary>
    public int FirstReminderHours { get; set; } = 24;
    /// <summary>Hours before the appointment for the second reminder (0 = off).</summary>
    public int SecondReminderHours { get; set; } = 2;
    /// <summary>No texts between these clinic-local times (US TCPA: 9 PM – 8 AM recipient time).</summary>
    public TimeOnly QuietHoursStart { get; set; } = new(21, 0);
    public TimeOnly QuietHoursEnd { get; set; } = new(8, 0);
}

/// <summary>
/// The message log and outbox: every email and text is stored here first, then sent by a background job with
/// retries. It is also the record of what was sent to whom, and when.
/// </summary>
public sealed class OutboundMessage : BaseEntity, ITenantEntity
{
    public Guid? ClinicId { get; set; }
    public MessageChannel Channel { get; set; }
    public MessageKind Kind { get; set; }
    /// <summary>Email address or E.164 phone number.</summary>
    public string To { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public string Body { get; set; } = string.Empty;
    /// <summary>For invites: the body contains a one-time link, so it is replaced with a redacted copy once sent.</summary>
    public bool RedactBodyAfterSend { get; set; }

    public Guid? PatientId { get; set; }
    public Guid? AppointmentId { get; set; }
    public Guid? UserId { get; set; }
    /// <summary>For reminders: hours before the appointment (24, 2...). Prevents sending the same reminder twice.</summary>
    public int? ReminderHours { get; set; }

    public MessageStatus Status { get; set; } = MessageStatus.Pending;
    public DateTime NotBeforeUtc { get; set; }
    public int Attempts { get; set; }
    public DateTime? ClaimedAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public string? LastError { get; set; }
    public string? ProviderMessageId { get; set; }
}
