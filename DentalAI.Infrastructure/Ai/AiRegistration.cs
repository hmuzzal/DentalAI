using DentalAI.Application.Common.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DentalAI.Infrastructure.Ai;

public static class AiRegistration
{
    /// <summary>
    /// Registers the <see cref="IChatModel"/> chosen by <c>Ai:Provider</c>. A provider that isn't built yet, or one
    /// missing its settings, stops the API at startup instead of failing on the first patient message.
    /// </summary>
    public static void AddAiAssistant(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(AiOptions.SectionName);
        services.Configure<AiOptions>(section);
        var options = section.Get<AiOptions>() ?? new AiOptions();
        var timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 5, 600));

        services.AddSingleton<IAssistantConversationStore, MemoryConversationStore>();
        services.AddSingleton(new AssistantLimits(
            Math.Clamp(options.MaxMessageLength, 50, 10_000),
            Math.Clamp(options.MaxModelCallsPerMessage, 1, 20)));

        switch (Normalize(options.Provider))
        {
            case AiProviders.None:
                services.AddSingleton<IChatModel, DisabledChatModel>();
                break;

            case AiProviders.Ollama:
                services.AddHttpClient<IChatModel, OllamaChatModel>(client =>
                {
                    client.BaseAddress = BaseAddress(options.Ollama.BaseUrl, "Ai:Ollama:BaseUrl");
                    client.Timeout = timeout;
                });
                break;

            case AiProviders.Gemini:
                if (string.IsNullOrWhiteSpace(options.Gemini.ApiKey))
                    throw new InvalidOperationException(
                        "Ai:Provider is \"Gemini\" but Ai:Gemini:ApiKey is empty. Set it in an environment variable " +
                        "(Ai__Gemini__ApiKey) or user secrets, not in appsettings.json.");
                services.AddHttpClient<IChatModel, GeminiChatModel>(client =>
                {
                    client.BaseAddress = BaseAddress(options.Gemini.BaseUrl, "Ai:Gemini:BaseUrl");
                    client.Timeout = timeout;
                });
                break;

            case AiProviders.OpenAI:
            case AiProviders.Claude:
                throw new InvalidOperationException(
                    $"Ai:Provider \"{options.Provider}\" is planned but not built yet. Use \"Ollama\" or \"Gemini\", or \"None\" to turn the assistant off.");

            default:
                throw new InvalidOperationException(
                    $"Ai:Provider \"{options.Provider}\" is not known. Use None, Ollama or Gemini (OpenAI and Claude are planned).");
        }
    }

    private static string Normalize(string? provider)
    {
        var value = (provider ?? string.Empty).Trim();
        if (value.Length == 0) return AiProviders.None;
        string[] known = [AiProviders.None, AiProviders.Ollama, AiProviders.Gemini, AiProviders.OpenAI, AiProviders.Claude];
        return known.FirstOrDefault(k => string.Equals(k, value, StringComparison.OrdinalIgnoreCase)) ?? value;
    }

    private static Uri BaseAddress(string? url, string setting)
    {
        if (!Uri.TryCreate((url ?? string.Empty).Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"{setting} must be an absolute URL.");
        return uri;
    }
}
