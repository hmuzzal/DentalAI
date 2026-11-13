using DentalAI.Application.Common.People;
using Xunit;

namespace DentalAI.Application.Tests.Common;

/// <summary>"Dr." is added once, even when it was typed into the doctor's name.</summary>
public sealed class DoctorNamesTests
{
    [Theory]
    [InlineData("Sarah", "Ahmed", "Dr. Sarah Ahmed")]
    [InlineData("Dr.", "Sarah Ahmed", "Dr. Sarah Ahmed")]
    [InlineData("Dr. Sarah", "Ahmed", "Dr. Sarah Ahmed")]
    [InlineData("dr", "Ahmed", "Dr. Ahmed")]
    [InlineData("Doctor", "Ahmed", "Dr. Ahmed")]
    [InlineData("  Sarah ", " Ahmed  ", "Dr. Sarah Ahmed")]
    [InlineData("Drew", "Barry", "Dr. Drew Barry")]
    [InlineData("Doctorow", "Cory", "Dr. Doctorow Cory")]
    [InlineData("", "", "")]
    [InlineData("Dr.", "", "")]
    public void WithTitle_AddsDrOnce(string first, string last, string expected) =>
        Assert.Equal(expected, DoctorNames.WithTitle(first, last));

    [Theory]
    [InlineData("Dr. Dr. Sarah Ahmed", "Dr. Sarah Ahmed")]
    [InlineData("Sarah Ahmed", "Dr. Sarah Ahmed")]
    [InlineData(null, "")]
    public void WithTitle_OnAFullName(string? name, string expected) => Assert.Equal(expected, DoctorNames.WithTitle(name));
}
