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
