namespace DentalAI.Application.Common.Ai;

/// <summary>
/// One patient's chat with the assistant, kept on the server (the browser only holds its id), so the history the
/// model sees can't be edited by the client and tool results never leave the server.
/// </summary>
public sealed class AssistantConversation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public List<ChatMessage> Messages { get; init; } = [];

    /// <summary>
    /// Short references ("S1", "D2", "A3") for the ids of services, dentists and appointments the model has seen.
    /// Small models copy these far more reliably than GUIDs.
    /// </summary>
    public Dictionary<string, Guid> Refs { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many patient messages this chat has had.</summary>
    public int Turn { get; set; }

    /// <summary>
    /// The booking, move or cancellation the assistant last put to the patient. It runs only when the model asks for
    /// exactly the same change in reply to the patient's very next message, and that message says yes.
    /// </summary>
    public PendingChange? Pending { get; set; }

    public string RefFor(char prefix, Guid id)
    {
        foreach (var (key, value) in Refs)
        {
            if (value == id && char.ToUpperInvariant(key[0]) == prefix) return key;
        }

        var next = $"{prefix}{Refs.Keys.Count(k => char.ToUpperInvariant(k[0]) == prefix) + 1}";
        Refs[next] = id;
        return next;
    }

    /// <summary>A short reference made by <see cref="RefFor"/>, or a raw id.</summary>
    public Guid? Resolve(char prefix, string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (Guid.TryParse(text, out var raw)) return raw;
        return char.ToUpperInvariant(text[0]) == prefix && Refs.TryGetValue(text, out var id) ? id : null;
    }
}

/// <summary>Keeps conversations for a while after their last message. Keyed by patient, so one patient can't open another's.</summary>
public interface IAssistantConversationStore
{
    AssistantConversation? Get(Guid patientId, Guid conversationId);
    void Save(Guid patientId, AssistantConversation conversation);
}

/// <param name="Key">What exactly would change, e.g. "book|service|dentist|start-utc".</param>
/// <param name="Turn">The patient message during which it was proposed.</param>
public sealed record PendingChange(string Key, int Turn);
