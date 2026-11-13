using DentalAI.Application.Common.Ai;
using Microsoft.Extensions.Caching.Memory;

namespace DentalAI.Infrastructure.Ai;

/// <summary>
/// Conversations in server memory, dropped 30 minutes after the last message. Nothing is written to the database.
/// With more than one API server this needs a shared store (e.g. Redis) or sticky sessions.
/// </summary>
public sealed class MemoryConversationStore(IMemoryCache cache) : IAssistantConversationStore
{
    private static readonly TimeSpan IdleLifetime = TimeSpan.FromMinutes(30);

    public AssistantConversation? Get(Guid patientId, Guid conversationId) =>
        cache.TryGetValue(Key(patientId, conversationId), out AssistantConversation? conversation) ? conversation : null;

    public void Save(Guid patientId, AssistantConversation conversation) =>
        cache.Set(Key(patientId, conversation.Id), conversation,
            new MemoryCacheEntryOptions { SlidingExpiration = IdleLifetime, AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(4) });

    private static string Key(Guid patientId, Guid conversationId) => $"assistant:{patientId:N}:{conversationId:N}";
}
