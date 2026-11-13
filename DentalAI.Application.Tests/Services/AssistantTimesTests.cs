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
