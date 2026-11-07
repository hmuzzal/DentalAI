using DentalAI.Domain.Entities;

namespace DentalAI.Application.DTOs.Notifications;

/// <param name="EmailLive">False when no email provider is configured: messages are logged, not delivered.</param>
/// <param name="SmsLive">False when no SMS provider is configured.</param>
public sealed record NotificationSettingsDto(
    bool EmailEnabled,
    bool SmsEnabled,
    bool ConfirmationsEnabled,
    bool RemindersEnabled,
    int FirstReminderHours,
    int SecondReminderHours,
    TimeOnly QuietHoursStart,
    TimeOnly QuietHoursEnd,
    bool EmailLive,
    bool SmsLive);

public sealed class UpdateNotificationSettingsRequest
{
    public bool EmailEnabled { get; set; } = true;
    public bool SmsEnabled { get; set; } = true;
    public bool ConfirmationsEnabled { get; set; } = true;
    public bool RemindersEnabled { get; set; } = true;
    public int FirstReminderHours { get; set; } = 24;
    public int SecondReminderHours { get; set; } = 2;
    public TimeOnly QuietHoursStart { get; set; } = new(21, 0);
    public TimeOnly QuietHoursEnd { get; set; } = new(8, 0);
}

public sealed record OutboundMessageDto(
    Guid Id,
    MessageChannel Channel,
    MessageKind Kind,
    string To,
    string? Subject,
    string Body,
    MessageStatus Status,
    Guid? PatientId,
    string? PatientName,
    Guid? AppointmentId,
    int? ReminderHours,
    DateTime CreatedAtUtc,
    DateTime NotBeforeUtc,
    DateTime? SentAtUtc,
    int Attempts,
    string? LastError);

public sealed class MessageListQuery
{
    public const int MaxPageSize = 100;
    public MessageStatus? Status { get; set; }
    public MessageChannel? Channel { get; set; }
    public Guid? PatientId { get; set; }
    public Guid? AppointmentId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

/// <summary>Sends a test message to an address or number (clinic admins), to check the setup.</summary>
public sealed class SendTestMessageRequest
{
    public MessageChannel Channel { get; set; }
    public string To { get; set; } = string.Empty;
}
