namespace DentalAI.Infrastructure.Ai;

/// <summary>
/// "Ai" settings: which model runs the portal assistant. <c>Provider</c> is one of <see cref="AiProviders"/>.
/// Ollama and Gemini are built; OpenAI and Claude are reserved for later and refuse to start until they are.
/// Keys belong in environment variables or user secrets (<c>Ai__Gemini__ApiKey</c>), never in appsettings.json.
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    public string Provider { get; set; } = AiProviders.None;

    /// <summary>How long one model call may take. Local models on a CPU can be slow.</summary>
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>Longest patient message, in characters (the portal page uses the same limit).</summary>
    public int MaxMessageLength { get; set; } = 1000;

    /// <summary>Most model calls one patient message may take (each tool round is one call) before the assistant gives up.</summary>
    public int MaxModelCallsPerMessage { get; set; } = 6;

    public OllamaOptions Ollama { get; set; } = new();
    public GeminiOptions Gemini { get; set; } = new();
    public HostedModelOptions OpenAI { get; set; } = new();
    public HostedModelOptions Claude { get; set; } = new() { Model = "claude-haiku-5-5" };
}

public static class AiProviders
{
    public const string None = "None";
    public const string Ollama = "Ollama";
    public const string Gemini = "Gemini";
    public const string OpenAI = "OpenAI";
    public const string Claude = "Claude";
}

/// <summary>A local model served by Ollama (https://ollama.com). Free; nothing leaves the machine.</summary>
public sealed class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>Needs a model that supports tool calling, e.g. qwen2.5:7b or llama3.1:8b (<c>ollama pull qwen2.5:7b</c>).</summary>
    public string Model { get; set; } = "qwen2.5:7b";

    /// <summary>
    /// Tokens the model keeps in view. Ollama's default (4096) is too small for the instructions, tools and a chat;
    /// past it, Ollama silently drops the start of the conversation. More context needs more (GPU) memory.
    /// </summary>
    public int ContextLength { get; set; } = 16384;
}

/// <summary>Google Gemini through the Generative Language API. Has a free tier with rate limits.</summary>
public sealed class GeminiOptions
{
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gemini-2.5-flash";
}

public sealed class HostedModelOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
}
