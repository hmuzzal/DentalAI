using DentalAI.Application.Common.Ai;
using Xunit;

namespace DentalAI.Application.Tests.Services;

public sealed class AssistantConversationTests
{
    [Fact]
    public void Refs_AreShortStableAndPerKind()
    {
        var conversation = new AssistantConversation();
        var service = Guid.NewGuid();
        var otherService = Guid.NewGuid();
        var dentist = Guid.NewGuid();

        Assert.Equal("S1", conversation.RefFor('S', service));
        Assert.Equal("S2", conversation.RefFor('S', otherService));
        Assert.Equal("S1", conversation.RefFor('S', service));
        Assert.Equal("D1", conversation.RefFor('D', dentist));

        Assert.Equal(service, conversation.Resolve('S', "s1"));
        Assert.Equal(dentist, conversation.Resolve('D', " D1 "));
    }

    [Fact]
    public void Resolve_AcceptsRawIds_AndRejectsUnknownOrWrongKindRefs()
    {
        var conversation = new AssistantConversation();
        var dentist = Guid.NewGuid();
        conversation.RefFor('D', dentist);

        Assert.Equal(dentist, conversation.Resolve('D', dentist.ToString()));
        Assert.Null(conversation.Resolve('S', "D1"));
        Assert.Null(conversation.Resolve('D', "D9"));
        Assert.Null(conversation.Resolve('D', ""));
        Assert.Null(conversation.Resolve('D', null));
    }
}
