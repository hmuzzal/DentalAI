using DentalAI.Application.Services;
using Xunit;

namespace DentalAI.Application.Tests.Services;

/// <summary>Whether the patient's reply agrees to the change the assistant put to them.</summary>
public sealed class AssistantConfirmationTests
{
    [Theory]
    [InlineData("Yes")]
    [InlineData("yes please")]
    [InlineData("Yep, go ahead")]
    [InlineData("ok")]
    [InlineData("Sure!")]
    [InlineData("Sounds good")]
    [InlineData("That works, thanks")]
    [InlineData("Please do")]
    [InlineData("Yes, cancel it")]
    [InlineData("Actually yes, go ahead")]
    public void Yes_IsAYes(string message) => Assert.True(ReceptionistService.SaysYes(message));

    [Theory]
    [InlineData("The first one please")]
    [InlineData("Tuesday at 3")]
    [InlineData("No")]
    [InlineData("No, don't cancel it")]
    [InlineData("Wait, is it with Dr. Ahmed?")]
    [InlineData("Yes but not that day")]
    [InlineData("Ok, make it 4 PM instead")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingElse_IsNot(string? message) => Assert.False(ReceptionistService.SaysYes(message));
}

/// <summary>Spotting replies that call a visit confirmed, which online bookings aren't until the clinic says so.</summary>
public sealed class AssistantConfirmedClaimTests
{
    [Theory]
    [InlineData("Your appointment with Dr. Sarah Ahmed on Friday, Nov 20 is confirmed.")]
    [InlineData("Yes, it's confirmed!")]
    [InlineData("You're all confirmed for Tuesday.")]
    [InlineData("Your visit has been confirmed.")]
    [InlineData("We have confirmed your appointment for a teeth cleaning on Friday, November 20 at 2:15 PM. Is this correct?")] // seen
    public void Claims_AreSpotted(string reply) => Assert.True(ReceptionistService.SaysConfirmed(reply));

    [Theory]
    [InlineData("Your visit is booked and waiting for the clinic to confirm it.")]
    [InlineData("It's pending until the clinic confirms.")]
    [InlineData("It hasn't been confirmed yet.")]
    [InlineData("I'll book 2:15 PM on Friday, Nov 20. Is that confirmed with you?")]
    [InlineData("The clinic will confirm your appointment shortly.")]
    [InlineData(null)]
    public void OtherReplies_AreNot(string? reply) => Assert.False(ReceptionistService.SaysConfirmed(reply));
}
