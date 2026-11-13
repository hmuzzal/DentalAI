using System.Text.Json.Nodes;
using DentalAI.Application.Common.Ai;
using DentalAI.Infrastructure.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DentalAI.Api.IntegrationTests;

/// <summary>
/// The Ollama and Gemini adapters turn the same conversation into each provider's wire format and read their
/// answers back. No network: these check the JSON shapes only.
/// </summary>
public sealed class ChatModelAdapterTests
{
    private static readonly ChatToolCall Call = new("call_1", "find_open_times", """{"service_id":"S1","date":"2026-11-20"}""", "sig-abc");

    private static readonly ChatRequest Conversation = new(
        "You are the receptionist.",
        [
            ChatMessage.User("Any time on Friday?"),
            ChatMessage.Assistant(null, [Call]),
            ChatMessage.ToolResult(Call, """{"dentists":[]}"""),
            ChatMessage.Assistant("Nothing on Friday, sorry.")
        ],
        [
            new ChatTool("list_services", "Lists services.", """{"type":"object","properties":{}}"""),
            new ChatTool("find_open_times", "Finds times.",
                """{"type":"object","properties":{"service_id":{"type":"string"}},"required":["service_id"]}""")
        ]);

    [Fact]
    public void Ollama_SendsSystemPromptToolsAndToolResults()
    {
        var body = OllamaChatModel.BuildRequest(Conversation, "qwen2.5:7b");

        Assert.Equal("qwen2.5:7b", body["model"]!.GetValue<string>());
        Assert.False(body["stream"]!.GetValue<bool>());
        var messages = body["messages"]!.AsArray();
        Assert.Equal(new[] { "system", "user", "assistant", "tool", "assistant" }, messages.Select(m => m!["role"]!.GetValue<string>()));
        Assert.Equal("S1", messages[2]!["tool_calls"]![0]!["function"]!["arguments"]!["service_id"]!.GetValue<string>());
        Assert.Equal("find_open_times", messages[3]!["tool_name"]!.GetValue<string>());
        Assert.Equal(2, body["tools"]!.AsArray().Count);
        Assert.Equal(16384, body["options"]!["num_ctx"]!.GetValue<int>());
    }

    [Fact]
    public void Ollama_ReadsTextAndToolCalls_WithArgumentsAsObjectOrString()
    {
        var reply = OllamaChatModel.ReadResponse(JsonNode.Parse("""
            {"message":{"role":"assistant","content":"","tool_calls":[
              {"function":{"name":"list_services","arguments":{}}},
              {"function":{"name":"find_open_times","arguments":"{\"service_id\":\"S2\"}"}}
            ]},"done":true}
            """));

        Assert.Equal(2, reply.ToolCalls.Count);
        Assert.Equal("list_services", reply.ToolCalls[0].Name);
        Assert.Contains("S2", reply.ToolCalls[1].ArgumentsJson);
        Assert.Equal("Hello", OllamaChatModel.ReadResponse(JsonNode.Parse("""{"message":{"content":"Hello"}}""")).Text);
    }

    [Fact]
    public void Gemini_GroupsToolResults_EchoesThoughtSignatures_AndSkipsEmptySchemas()
    {
        var body = GeminiChatModel.BuildRequest(Conversation);

        Assert.Equal("You are the receptionist.", body["systemInstruction"]!["parts"]![0]!["text"]!.GetValue<string>());
        var contents = body["contents"]!.AsArray();
        Assert.Equal(new[] { "user", "model", "user", "model" }, contents.Select(c => c!["role"]!.GetValue<string>()));

        var callPart = contents[1]!["parts"]![0]!;
        Assert.Equal("find_open_times", callPart["functionCall"]!["name"]!.GetValue<string>());
        Assert.Equal("sig-abc", callPart["thoughtSignature"]!.GetValue<string>());
        Assert.Equal("find_open_times", contents[2]!["parts"]![0]!["functionResponse"]!["name"]!.GetValue<string>());

        var declarations = body["tools"]![0]!["functionDeclarations"]!.AsArray();
        Assert.Null(declarations[0]!["parameters"]);
        Assert.NotNull(declarations[1]!["parameters"]);
    }

    [Fact]
    public void Gemini_ReadsCallsWithSignatures_AndIgnoresThoughts()
    {
        var reply = GeminiChatModel.ReadResponse(JsonNode.Parse("""
            {"candidates":[{"content":{"role":"model","parts":[
              {"text":"thinking...","thought":true},
              {"text":"Let me check."},
              {"functionCall":{"name":"list_dentists","args":{}},"thoughtSignature":"sig-1"}
            ]}}]}
            """));

        Assert.Equal("Let me check.", reply.Text);
        var call = Assert.Single(reply.ToolCalls);
        Assert.Equal("list_dentists", call.Name);
        Assert.Equal("sig-1", call.ProviderData);

        // A blocked answer has no content: no text, no calls.
        var blocked = GeminiChatModel.ReadResponse(JsonNode.Parse("""{"promptFeedback":{"blockReason":"SAFETY"}}"""));
        Assert.Null(blocked.Text);
        Assert.Empty(blocked.ToolCalls);
    }

    [Theory]
    [InlineData("None", "None")]
    [InlineData("", "None")]
    [InlineData("ollama", "Ollama")]
    public void Provider_IsChosenByConfiguration(string provider, string expected)
    {
        Assert.Equal(expected, Build(new() { ["Ai:Provider"] = provider }).Provider);
    }

    [Fact]
    public void Gemini_NeedsAKey_AndPlannedProvidersRefuseToStart()
    {
        Assert.Equal("Gemini", Build(new() { ["Ai:Provider"] = "Gemini", ["Ai:Gemini:ApiKey"] = "test-key" }).Provider);
        Assert.Throws<InvalidOperationException>(() => Build(new() { ["Ai:Provider"] = "Gemini" }));
        Assert.Throws<InvalidOperationException>(() => Build(new() { ["Ai:Provider"] = "Claude" }));
        Assert.Throws<InvalidOperationException>(() => Build(new() { ["Ai:Provider"] = "OpenAI" }));
        Assert.Throws<InvalidOperationException>(() => Build(new() { ["Ai:Provider"] = "Mystery" }));
    }

    [Fact]
    public void Limits_ComeFromSettings_WithDefaults_AndAreKeptSensible()
    {
        Assert.Equal(new AssistantLimits(1000, 6), Services(new()).GetRequiredService<AssistantLimits>());
        Assert.Equal(new AssistantLimits(500, 3), Services(new()
        {
            ["Ai:MaxMessageLength"] = "500", ["Ai:MaxModelCallsPerMessage"] = "3"
        }).GetRequiredService<AssistantLimits>());
        Assert.Equal(new AssistantLimits(50, 1), Services(new()
        {
            ["Ai:MaxMessageLength"] = "0", ["Ai:MaxModelCallsPerMessage"] = "-5"
        }).GetRequiredService<AssistantLimits>());
    }

    private static IChatModel Build(Dictionary<string, string?> settings) => Services(settings).GetRequiredService<IChatModel>();

    private static ServiceProvider Services(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddMemoryCache();
        services.AddAiAssistant(configuration);
        return services.BuildServiceProvider();
    }
}
