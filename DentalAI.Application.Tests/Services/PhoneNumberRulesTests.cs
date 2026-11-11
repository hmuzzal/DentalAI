using DentalAI.Application.Common.Phone;
using Xunit;

namespace DentalAI.Application.Tests.Services;

public sealed class PhoneNumberRulesTests
{
    [Theory]
    [InlineData("(212) 555-0142", "US", "+12125550142")]
    [InlineData("212.555.0142", "US", "+12125550142")]
    [InlineData("1 212 555 0142", "US", "+12125550142")]
    [InlineData("+1 (212) 555-0142", "US", "+12125550142")]
    [InlineData("+44 20 7946 0958", "US", "+442079460958")] // an explicit country code wins over the clinic's
    [InlineData("01711-000101", "BD", "+8801711000101")]
    [InlineData("2125550142", null, "+12125550142")]       // no clinic country: US
    public void TryNormalize_StoresE164(string input, string? region, string expected)
    {
        Assert.True(PhoneNumberRules.TryNormalize(input, region, out var e164));
        Assert.Equal(expected, e164);
    }

    [Theory]
    [InlineData("555-0142", "US")]          // missing area code
    [InlineData("(555) 010-0100", "US")]    // 555 is not a real area code
    [InlineData("12345", "US")]
    [InlineData("call me", "US")]
    [InlineData("", "US")]
    public void TryNormalize_RejectsNumbersThatCantBeDialled(string input, string region)
    {
        Assert.False(PhoneNumberRules.TryNormalize(input, region, out _));
    }

    [Theory]
    [InlineData("+12125550142", true)]
    [InlineData("+8801711000101", true)]
    [InlineData("(212) 555-0142", false)]  // old format
    [InlineData("+1-555-0101", false)]     // not a real number
    [InlineData("+15550100100", false)]    // E.164 shape, but 555 is no area code
    [InlineData("", false)]
    public void IsStoredCorrectly_OnlyForValidE164(string phone, bool expected)
    {
        Assert.Equal(expected, PhoneNumberRules.IsStoredCorrectly(phone));
    }

    [Fact]
    public void Countries_ListTheUnitedStatesFirst()
    {
        var countries = PhoneNumberRules.Countries();

        Assert.Equal(("US", "United States"), countries[0]);
        Assert.Contains(countries, c => c.Code == "CA");
        Assert.Contains(countries, c => c.Code == "BD");
        Assert.True(PhoneNumberRules.IsSupportedRegion("us"));
        Assert.False(PhoneNumberRules.IsSupportedRegion("XX"));
    }
}
