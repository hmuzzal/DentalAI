using DentalAI.Application.Services;
using Xunit;

namespace DentalAI.Application.Tests.Services;

/// <summary>The few open times the assistant is given to offer.</summary>
public sealed class AssistantTimesTests
{
    private static string[] Times(int count) =>
        Enumerable.Range(0, count).Select(i => new TimeOnly(12, 0).AddMinutes(15 * i).ToString("HH:mm")).ToArray();

    [Fact]
    public void AFewTimes_AreAllKept() => Assert.Equal(Times(3), ReceptionistService.SpreadTimes(Times(3), 4));

    [Fact]
    public void ManyTimes_AreSpreadFromTheFirstToTheLast() =>
        // 12:00 to 16:00 every 15 minutes (17 times), as in the real test that read them all out.
        Assert.Equal(new[] { "12:00", "13:15", "14:45", "16:00" }, ReceptionistService.SpreadTimes(Times(17), 4));

    [Fact]
    public void FiveTimes_GiveFourDifferentOnes() =>
        Assert.Equal(4, ReceptionistService.SpreadTimes(Times(5), 4).Distinct().Count());
}

/// <summary>The time a patient picks, read by the server so the booking doesn't depend on the model.</summary>
public sealed class AssistantPickedTimeTests
{
    [Theory]
    [InlineData("Can I have 2:15 instead?", "14:15")]
    [InlineData("the 3 pm one please", "15:00")]
    [InlineData("10:30am", "10:30")]
    [InlineData("9:00 works", "09:00")]
    [InlineData("12:00 please", "12:00")]
    [InlineData("Monday at 2:15", "14:15")]
    public void APick_IsRead(string message, string expected) =>
        Assert.Equal(TimeOnly.Parse(expected), ReceptionistService.PickedTime(message));

    [Theory]
    [InlineData("Anything after 3pm?")]
    [InlineData("Is there something in the afternoon?")]
    [InlineData("2:15 or 3:00?")]
    [InlineData("I'm 35 years old")]
    [InlineData("the first one please")]
    [InlineData(null)]
    public void NoSinglePick_IsNull(string? message) => Assert.Null(ReceptionistService.PickedTime(message));
}
