namespace DentalAI.Application.DTOs.Portal;

/// <summary>Whether the portal shows the assistant, and the longest message it takes. <c>Provider</c> is for support only.</summary>
public sealed record AssistantStatusDto(bool Enabled, string Provider, int MaxMessageLength);

public sealed class AssistantMessageRequest
{
    /// <summary>Null to start a new chat. An expired or unknown id also starts a new one.</summary>
    public Guid? ConversationId { get; set; }

    public string Message { get; set; } = string.Empty;
}

/// <param name="AppointmentsChanged">The assistant booked, moved or cancelled something: refresh the visit list.</param>
public sealed record AssistantReplyDto(Guid ConversationId, string Reply, bool AppointmentsChanged);
