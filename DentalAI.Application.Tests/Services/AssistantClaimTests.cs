using DentalAI.Application.Services;
using Xunit;

namespace DentalAI.Application.Tests.Services;

/// <summary>Spotting replies that say a visit was booked, moved or cancelled, so they can be checked against what was done.</summary>
public sealed class AssistantClaimTests
{
    [Theory]
    [InlineData("Your appointment on Monday has been cancelled.")]
    [InlineData("Your cleaning has been booked for Tuesday at 9:00 AM.")]
    [InlineData("I've rescheduled your visit to Friday.")]
    [InlineData("You're booked!")]
    [InlineData("Your visit was successfully moved to 3:00 PM.")]
    [InlineData("Your appointment is booked with Dr. Sarah Ahmed for a teeth cleaning at 2:15 PM on Friday, Nov 20.")] // slipped through once
    [InlineData("Your visit is now scheduled for Friday.")]
    [InlineData("I booked you in for 3:00 PM.")]
    [InlineData("Your cleaning was canceled.")]
    public void Claims_AreSpotted(string reply) => Assert.True(ReceptionistService.ClaimsAChange(reply));

    [Theory]
    [InlineData("Is it okay if I cancel your appointment on Monday?")]
    [InlineData("Would you like me to book 10:30 AM?")]
    [InlineData("Your next visit is on Monday at 10:30 AM with Dr. Ahmed.")]
    [InlineData("Appointments can be cancelled online until 24 hours before.")]
    [InlineData("")]
    [InlineData(null)]
    public void QuestionsAndFacts_AreNotClaims(string? reply) => Assert.False(ReceptionistService.ClaimsAChange(reply));

    [Theory]
    [InlineData("Let's check your upcoming appointments.")]
    [InlineData("I'll list the services for you.")]
    [InlineData("Let me find some times for you.")]
    [InlineData("Could you please provide the ID of the appointment?")]
    [InlineData("One moment please.")]
    public void Stalls_AreSpotted(string reply) => Assert.True(ReceptionistService.StallsInsteadOfActing(reply));

    [Theory]
    [InlineData("Your next visit is on Monday at 10:30 AM.")]
    [InlineData("I have 9:00, 9:30 or 10:00 AM. Which would you like?")]
    [InlineData("Shall I cancel your cleaning on Monday?")]
    public void Answers_AreNotStalls(string reply) => Assert.False(ReceptionistService.StallsInsteadOfActing(reply));
}

/// <summary>Whether the patient's message asks for a change, so that a reply claiming one must be backed by a tool.</summary>
public sealed class AssistantChangeRequestTests
{
    [Theory]
    [InlineData("Book me a cleaning on Friday")]
    [InlineData("Can you cancel my visit?")]
    [InlineData("I need to move my appointment")]
    [InlineData("Please reschedule it")]
    public void Requests_AreSpotted(string message) => Assert.True(ReceptionistService.AsksForAChange(message));

    [Theory]
    [InlineData("When is my next visit?")]
    [InlineData("Is my Friday visit confirmed?")]
    [InlineData("What do I owe?")]
    public void Questions_AreNot(string message) => Assert.False(ReceptionistService.AsksForAChange(message));
}

/// <summary>A reply that asks to go ahead with one change must come after the tool checked it.</summary>
public sealed class AssistantProposalTests
{
    [Theory]
    [InlineData("We can book your cleaning at 2:15 PM with Dr. Sarah Ahmed on Friday, November 20. Would you like to confirm this time?")] // seen
    [InlineData("Shall I book 10:30 AM on Monday for you?")]
    [InlineData("Would you like me to cancel your cleaning on Tue, Nov 17 at 2:00 PM?")]
    [InlineData("The appointment has been booked for you at 2:15 PM on Friday, November 20 with Dr. Sarah Ahmed. Is this correct? Please confirm.")] // seen
    public void Proposals_AreSpotted(string reply) => Assert.True(ReceptionistService.ProposesAChange(reply));

    [Theory]
    [InlineData("I have 12:00 PM, 1:30 PM, 3:00 PM or 4:30 PM. Would you like to book one of these times?")]
    [InlineData("Your next visit is on Tuesday at 2:00 PM.")]
    [InlineData("Would you like me to check another day?")]
    [InlineData(null)]
    public void OtherReplies_AreNot(string? reply) => Assert.False(ReceptionistService.ProposesAChange(reply));
}

public sealed class AssistantTimeMentionTests
{
    [Theory]
    [InlineData("Can I have 2:15 instead?", true)]
    [InlineData("3 pm works", true)]
    [InlineData("the 10am one", true)]
    [InlineData("When is my next visit?", false)]
    [InlineData("I'm 35 years old", false)]
    public void Times_AreSpotted(string message, bool expected) => Assert.Equal(expected, ReceptionistService.MentionsATime(message));
}
