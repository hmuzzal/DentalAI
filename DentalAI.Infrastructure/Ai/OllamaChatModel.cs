using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using DentalAI.Application.Common.Ai;
using Microsoft.Extensions.Options;

namespace DentalAI.Infrastructure.Ai;

/// <summary>A local model through Ollama's <c>/api/chat</c> endpoint, with tool calling.</summary>
public sealed class OllamaChatModel(HttpClient http, IOptions<AiOptions> options) : IChatModel
{
    private readonly OllamaOptions _settings = options.Value.Ollama;

    public string Provider => AiProviders.Ollama;
    public bool IsEnabled => true;

    public async Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        var body = BuildRequest(request, _settings.Model, _settings.ContextLength);
        JsonNode? reply;
        try
        {
            using var response = await http.PostAsJsonAsync("api/chat", body, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new ChatModelException($"Ollama answered {(int)response.StatusCode}: {Shorten(detail)}");
            }

            reply = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new ChatModelException($"Ollama could not be reached at {_settings.BaseUrl}. Is it running?", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ChatModelException("Ollama took too long to answer.", ex);
        }
        catch (JsonException ex)
        {
            throw new ChatModelException("Ollama sent an answer that could not be read.", ex);
        }

        return ReadResponse(reply);
    }

    public static JsonObject BuildRequest(ChatRequest request, string model, int contextLength = 16384)
    {
        var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = request.SystemPrompt } };
        foreach (var message in request.Messages)
        {
            switch (message.Role)
            {
                case ChatRole.User:
                    messages.Add(new JsonObject { ["role"] = "user", ["content"] = message.Text ?? string.Empty });
                    break;
                case ChatRole.Assistant:
                    var assistant = new JsonObject { ["role"] = "assistant", ["content"] = message.Text ?? string.Empty };
                    if (message.ToolCalls is { Count: > 0 } calls)
                    {
                        assistant["tool_calls"] = new JsonArray(calls.Select(c => (JsonNode)new JsonObject
                        {
                            ["function"] = new JsonObject { ["name"] = c.Name, ["arguments"] = ParseObject(c.ArgumentsJson) }
                        }).ToArray());
                    }
                    messages.Add(assistant);
                    break;
                case ChatRole.Tool:
                    messages.Add(new JsonObject
                    {
                        ["role"] = "tool",
                        ["tool_name"] = message.ToolName,
                        ["content"] = message.Text ?? "{}"
                    });
                    break;
            }
        }

        return new JsonObject
        {
            ["model"] = model,
            ["stream"] = false,
            ["messages"] = messages,
            ["tools"] = new JsonArray(request.Tools.Select(t => (JsonNode)new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["parameters"] = JsonNode.Parse(t.ParametersJsonSchema)
                }
            }).ToArray()),
            // Low temperature: a receptionist should be predictable, not creative.
            ["options"] = new JsonObject { ["temperature"] = 0.2, ["num_ctx"] = contextLength }
        };
    }

    public static ChatResponse ReadResponse(JsonNode? reply)
    {
        var message = reply?["message"] ?? throw new ChatModelException("Ollama's answer had no message.");
        var text = message["content"]?.GetValue<string>();
        var calls = new List<ChatToolCall>();
        if (message["tool_calls"] is JsonArray toolCalls)
        {
            foreach (var call in toolCalls)
            {
                var function = call?["function"];
                var name = function?["name"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(name)) continue;

                // Arguments are normally an object; some models send them as a JSON string.
                var arguments = function!["arguments"] switch
                {
                    JsonObject obj => obj.ToJsonString(),
                    JsonValue value when value.TryGetValue<string>(out var s) => s,
                    _ => "{}"
                };
                var id = call!["id"]?.GetValue<string>() ?? $"call_{Guid.NewGuid():N}";
                calls.Add(new ChatToolCall(id, name, arguments));
            }
        }

        return new ChatResponse(text, calls);
    }

    private static JsonNode ParseObject(string json)
    {
        try
        {
            return JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }

    private static string Shorten(string text) => text.Length <= 300 ? text : text[..300] + "…";
}
