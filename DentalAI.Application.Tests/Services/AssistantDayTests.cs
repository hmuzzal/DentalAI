using DentalAI.Application.Services;
using Xunit;

namespace DentalAI.Application.Tests.Services;

/// <summary>Picking the patient's visits by the day they name ("my Friday visit").</summary>
public sealed class AssistantDayTests
{
    private static readonly DateOnly Today = new(2026, 11, 13); // a Friday
    private static readonly DateTime FridayNext = new(2026, 11, 20, 14, 15, 0);
    private static readonly DateTime TuesdayNext = new(2026, 11, 17, 14, 0, 0);

    [Theory]
    [InlineData("Friday")]
    [InlineData("friday")]
    [InlineData("next Friday")]
    [InlineData("Fri")]
    [InlineData("fri.")]
    [InlineData("Fridays")]
    [InlineData("2026-11-20")]
    [InlineData("Nov 20")]
    [InlineData("Fri, Nov 18")] // the model's own wording, with a wrong date: the weekday wins
    [InlineData("my Friday visit")]
    public void NamesOfThatDay_Match(string text)
    {
        var matches = ReceptionistService.DayMatcher(text, Today);
        Assert.NotNull(matches);
        Assert.True(matches(FridayNext));
        Assert.False(matches(TuesdayNext));
    }

    [Fact]
    public void TodayAndTomorrow_AreTheClinicsDays()
    {
        Assert.True(ReceptionistService.DayMatcher("today", Today)!(new DateTime(2026, 11, 13, 16, 0, 0)));
        Assert.True(ReceptionistService.DayMatcher("tomorrow", Today)!(new DateTime(2026, 11, 14, 9, 0, 0)));
        Assert.False(ReceptionistService.DayMatcher("tomorrow", Today)!(FridayNext));
    }

    [Theory]
    [InlineData("Tues")]
    [InlineData("thurs")]
    public void ShortNames_PickTheRightDay(string text) =>
        Assert.Equal(text.StartsWith("tu", StringComparison.OrdinalIgnoreCase), ReceptionistService.DayMatcher(text, Today)!(TuesdayNext));

    [Theory]
    [InlineData("soon")]
    [InlineData("t")]
    [InlineData("2026-13-01")]
    [InlineData("Nov 31")]
    public void Unreadable_IsNull(string text) => Assert.Null(ReceptionistService.DayMatcher(text, Today));
}

/// <summary>The server, not the model, turns "Friday" into a date.</summary>
public sealed class AssistantDateTests
{
    private static readonly DateOnly Saturday = new(2026, 11, 14);
    private static readonly DateOnly Friday = new(2026, 11, 13);

    [Theory]
    [InlineData("Friday", "2026-11-20")]
    [InlineData("next Friday", "2026-11-20")]   // said on a Saturday: the coming Friday
    [InlineData("fri", "2026-11-20")]
    [InlineData("Fri, Nov 22", "2026-11-20")]   // the model's wrong date loses to the weekday
    [InlineData("Saturday", "2026-11-14")]      // today
    [InlineData("tomorrow", "2026-11-15")]
    [InlineData("2026-11-22", "2026-11-22")]
    [InlineData("Nov 30", "2026-11-30")]
    public void Days_BecomeDates(string text, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), ReceptionistService.ResolveDate(text, Saturday));

    [Fact]
    public void NextWeekday_OnThatWeekday_IsAWeekOn()
    {
        Assert.Equal(Friday, ReceptionistService.ResolveDate("Friday", Friday));
        Assert.Equal(Friday.AddDays(7), ReceptionistService.ResolveDate("next Friday", Friday));
    }

    [Theory]
    [InlineData("Can I get a cleaning next Friday afternoon?", DayOfWeek.Friday)]
    [InlineData("anything on tues?", DayOfWeek.Tuesday)]
    public void TheWeekdayThePatientNamed_IsFound(string message, DayOfWeek expected) =>
        Assert.Equal(expected, ReceptionistService.WeekdayNamed(message));

    [Theory]
    [InlineData("Can I move my Tuesday visit to Friday?")] // two days: no single answer
    [InlineData("2:15 please")]
    [InlineData(null)]
    public void NoSingleWeekday_IsNull(string? message) => Assert.Null(ReceptionistService.WeekdayNamed(message));
}
