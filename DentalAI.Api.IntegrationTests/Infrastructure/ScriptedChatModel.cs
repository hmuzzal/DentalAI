using System.Text.Json;
using DentalAI.Application.Common.Ai;

namespace DentalAI.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Stands in for Ollama or Gemini in the tests: each model call runs the next scripted step, which sees the whole
/// request (system prompt, history, tool results) and returns text or tool calls. Tests in the API collection run
/// one at a time, so a test sets the script it needs.
/// </summary>
public sealed class ScriptedChatModel : IChatModel
{
    private readonly Queue<Func<ChatRequest, ChatResponse>> _steps = new();

    public bool Enabled { get; set; } = true;
    public List<ChatRequest> Requests { get; } = [];

    public string Provider => "Scripted";
    public bool IsEnabled => Enabled;

    public void Script(params Func<ChatRequest, ChatResponse>[] steps)
    {
        Enabled = true;
        _steps.Clear();
        Requests.Clear();
        foreach (var step in steps) _steps.Enqueue(step);
    }

    public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (!_steps.TryDequeue(out var step)) throw new ChatModelException("The script has no more steps.");
        return Task.FromResult(step(request));
    }

    public static ChatResponse Say(string text) => new(text, []);

    public static ChatResponse Call(params (string Name, object Args)[] calls) =>
        new(null, calls.Select(c => new ChatToolCall($"call_{Guid.NewGuid():N}", c.Name, JsonSerializer.Serialize(c.Args))).ToList());

    /// <summary>The result of the most recent call to <paramref name="tool"/>.</summary>
    public static JsonElement LastResult(ChatRequest request, string tool)
    {
        var message = request.Messages.Last(m => m.Role == ChatRole.Tool && m.ToolName == tool);
        return JsonDocument.Parse(message.Text!).RootElement.Clone();
    }
}
