namespace DentalAI.Application.Common.Ai;

/// <summary>
/// A provider-neutral chat model with tool calling. Each provider (Ollama, Gemini, and later OpenAI and Claude) has
/// an adapter in Infrastructure that turns these types into its own wire format, so the assistant's logic, tools and
/// rules never depend on which model is behind it.
/// </summary>
public interface IChatModel
{
    /// <summary>"Ollama", "Gemini", ... or "None" when the assistant is turned off.</summary>
    string Provider { get; }

    bool IsEnabled { get; }

    /// <exception cref="ChatModelException">The provider could not be reached or refused the request.</exception>
    Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken);
}

public enum ChatRole
{
    User,
    Assistant,
    Tool
}

/// <param name="Id">The provider's call id, or one made up by the adapter when the provider has none.</param>
/// <param name="ArgumentsJson">A JSON object.</param>
/// <param name="ProviderData">Opaque data the provider needs back with this call (e.g. Gemini's thought signature).</param>
public sealed record ChatToolCall(string Id, string Name, string ArgumentsJson, string? ProviderData = null);

/// <summary>A user or assistant turn, or the result of one tool call (Role = Tool, Text = JSON object).</summary>
public sealed record ChatMessage(
    ChatRole Role,
    string? Text,
    IReadOnlyList<ChatToolCall>? ToolCalls = null,
    string? ToolCallId = null,
    string? ToolName = null)
{
    public static ChatMessage User(string text) => new(ChatRole.User, text);

    public static ChatMessage Assistant(string? text, IReadOnlyList<ChatToolCall>? toolCalls = null) =>
        new(ChatRole.Assistant, text, toolCalls is { Count: > 0 } ? toolCalls : null);

    public static ChatMessage ToolResult(ChatToolCall call, string resultJson) =>
        new(ChatRole.Tool, resultJson, null, call.Id, call.Name);
}

/// <param name="ParametersJsonSchema">A JSON Schema object: type, properties, required, descriptions.</param>
public sealed record ChatTool(string Name, string Description, string ParametersJsonSchema);

public sealed record ChatRequest(string SystemPrompt, IReadOnlyList<ChatMessage> Messages, IReadOnlyList<ChatTool> Tools);

public sealed record ChatResponse(string? Text, IReadOnlyList<ChatToolCall> ToolCalls);

public sealed class ChatModelException(string message, Exception? innerException = null) : Exception(message, innerException);

/// <summary>Used when <c>Ai:Provider</c> is "None": the portal hides the assistant.</summary>
public sealed class DisabledChatModel : IChatModel
{
    public string Provider => "None";
    public bool IsEnabled => false;

    public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken) =>
        throw new ChatModelException("The assistant is turned off.");
}

/// <summary>Assistant limits from the "Ai" settings (see AiOptions in Infrastructure).</summary>
/// <param name="MaxMessageLength">Longest patient message, in characters.</param>
/// <param name="MaxModelCallsPerMessage">Most model calls one patient message may take before the assistant gives up.</param>
public sealed record AssistantLimits(int MaxMessageLength = 1000, int MaxModelCallsPerMessage = 6);
