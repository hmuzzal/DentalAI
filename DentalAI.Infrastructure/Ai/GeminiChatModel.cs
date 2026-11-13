using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using DentalAI.Application.Common.Ai;
using Microsoft.Extensions.Options;

namespace DentalAI.Infrastructure.Ai;

/// <summary>
/// Google Gemini through the Generative Language API (<c>models/{model}:generateContent</c>), with function calling.
/// The free tier may use prompts to improve Google's models: use it with test data only, never real patients.
/// </summary>
public sealed class GeminiChatModel(HttpClient http, IOptions<AiOptions> options) : IChatModel
{
    private readonly GeminiOptions _settings = options.Value.Gemini;

    public string Provider => AiProviders.Gemini;
    public bool IsEnabled => true;

    public async Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post,
            $"v1beta/models/{Uri.EscapeDataString(_settings.Model)}:generateContent")
        {
            Content = JsonContent.Create(BuildRequest(request))
        };
        // The key goes in a header, not the URL, so it never shows up in request logs.
        message.Headers.Add("x-goog-api-key", _settings.ApiKey);

        JsonNode? reply;
        try
        {
            using var response = await http.SendAsync(message, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new ChatModelException(response.StatusCode == HttpStatusCode.TooManyRequests
                    ? "Gemini's rate limit was reached (the free tier allows only a few requests a minute)."
                    : $"Gemini answered {(int)response.StatusCode}: {Shorten(detail)}");
            }

            reply = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new ChatModelException("Gemini could not be reached.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ChatModelException("Gemini took too long to answer.", ex);
        }
        catch (JsonException ex)
        {
            throw new ChatModelException("Gemini sent an answer that could not be read.", ex);
        }

        return ReadResponse(reply);
    }

    public static JsonObject BuildRequest(ChatRequest request)
    {
        var contents = new JsonArray();
        JsonArray? pendingResults = null;

        foreach (var message in request.Messages)
        {
            if (message.Role == ChatRole.Tool)
            {
                // All results of one round go back together, in one user turn.
                if (pendingResults is null)
                {
                    pendingResults = new JsonArray();
                    contents.Add(new JsonObject { ["role"] = "user", ["parts"] = pendingResults });
                }

                var functionResponse = new JsonObject
                {
                    ["name"] = message.ToolName,
                    ["response"] = AsObject(message.Text)
                };
                if (IsProviderId(message.ToolCallId)) functionResponse["id"] = message.ToolCallId;
                pendingResults.Add(new JsonObject { ["functionResponse"] = functionResponse });
                continue;
            }

            pendingResults = null;
            var parts = new JsonArray();
            if (!string.IsNullOrWhiteSpace(message.Text)) parts.Add(new JsonObject { ["text"] = message.Text });

            if (message.Role == ChatRole.Assistant && message.ToolCalls is { Count: > 0 } calls)
            {
                foreach (var call in calls)
                {
                    var functionCall = new JsonObject { ["name"] = call.Name, ["args"] = AsObject(call.ArgumentsJson) };
                    if (IsProviderId(call.Id)) functionCall["id"] = call.Id;
                    var part = new JsonObject { ["functionCall"] = functionCall };
                    // Gemini's thinking models need their signature back with each call.
                    if (call.ProviderData is { Length: > 0 } signature) part["thoughtSignature"] = signature;
                    parts.Add(part);
                }
            }

            if (parts.Count == 0) parts.Add(new JsonObject { ["text"] = " " });
            contents.Add(new JsonObject { ["role"] = message.Role == ChatRole.User ? "user" : "model", ["parts"] = parts });
        }

        var declarations = new JsonArray();
        foreach (var tool in request.Tools)
        {
            var declaration = new JsonObject { ["name"] = tool.Name, ["description"] = tool.Description };
            // Gemini rejects an object schema with no properties: leave parameters out for tools without arguments.
            if (JsonNode.Parse(tool.ParametersJsonSchema) is JsonObject schema
                && schema["properties"] is JsonObject { Count: > 0 })
                declaration["parameters"] = schema;
            declarations.Add(declaration);
        }

        return new JsonObject
        {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = request.SystemPrompt }) },
            ["contents"] = contents,
            ["tools"] = new JsonArray(new JsonObject { ["functionDeclarations"] = declarations }),
            ["generationConfig"] = new JsonObject { ["temperature"] = 0.2 }
        };
    }

    public static ChatResponse ReadResponse(JsonNode? reply)
    {
        // A blocked prompt or response comes back with no content: treat it as "no answer".
        if (reply?["candidates"]?[0]?["content"]?["parts"] is not JsonArray parts) return new ChatResponse(null, []);

        var text = new List<string>();
        var calls = new List<ChatToolCall>();
        foreach (var part in parts)
        {
            if (part is null || part["thought"]?.GetValue<bool>() == true) continue;

            if (part["functionCall"] is JsonObject call && call["name"]?.GetValue<string>() is { Length: > 0 } name)
            {
                calls.Add(new ChatToolCall(
                    call["id"]?.GetValue<string>() ?? $"{LocalIdPrefix}{Guid.NewGuid():N}",
                    name,
                    (call["args"] as JsonObject)?.ToJsonString() ?? "{}",
                    part["thoughtSignature"]?.GetValue<string>()));
            }
            else if (part["text"]?.GetValue<string>() is { Length: > 0 } piece)
            {
                text.Add(piece);
            }
        }

        return new ChatResponse(text.Count == 0 ? null : string.Concat(text), calls);
    }

    /// <summary>Ids made up here (Gemini gave none) are not sent back to Gemini.</summary>
    private const string LocalIdPrefix = "local_";

    private static bool IsProviderId(string? id) => !string.IsNullOrEmpty(id) && !id.StartsWith(LocalIdPrefix, StringComparison.Ordinal);

    /// <summary>Gemini wants objects for call args and function responses; anything else is wrapped.</summary>
    private static JsonObject AsObject(string? json)
    {
        try
        {
            var node = JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            return node as JsonObject ?? new JsonObject { ["result"] = node };
        }
        catch (JsonException)
        {
            return new JsonObject { ["result"] = json };
        }
    }

    private static string Shorten(string text) => text.Length <= 300 ? text : text[..300] + "…";
}
