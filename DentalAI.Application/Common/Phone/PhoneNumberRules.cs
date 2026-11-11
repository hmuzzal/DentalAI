using System.Globalization;
using PhoneNumbers;

namespace DentalAI.Application.Common.Phone;

/// <summary>
/// Phone numbers are stored in E.164 (e.g. +12125550142), which SMS providers need. Numbers typed without a
/// country code are read in the clinic's country; numbers starting with + keep their own country.
/// </summary>
public static class PhoneNumberRules
{
    public const string DefaultRegion = "US";
    private static readonly PhoneNumberUtil Util = PhoneNumberUtil.GetInstance();
    private static readonly HashSet<string> Regions = Util.GetSupportedRegions().ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>True for a 2-letter country code that phone numbers can be parsed for.</summary>
    public static bool IsSupportedRegion(string? code) => code is { Length: 2 } && Regions.Contains(code);

    /// <summary>Parses <paramref name="input"/> and returns it in E.164, or false when it isn't a valid number.</summary>
    public static bool TryNormalize(string? input, string? region, out string e164)
    {
        e164 = string.Empty;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var defaultRegion = IsSupportedRegion(region) ? region!.ToUpperInvariant() : DefaultRegion;
        try
        {
            var number = Util.Parse(input.Trim(), defaultRegion);
            if (!Util.IsValidNumber(number)) return false;
            e164 = Util.Format(number, PhoneNumberFormat.E164);
            return true;
        }
        catch (NumberParseException)
        {
            return false;
        }
    }

    /// <summary>True when <paramref name="phone"/> is a valid number stored in E.164, the format SMS and portal sign-in need.</summary>
    public static bool IsStoredCorrectly(string? phone) =>
        phone is { Length: > 0 } && phone[0] == '+' && TryNormalize(phone, DefaultRegion, out var e164) && e164 == phone;

    /// <summary>For messages: US/Canada numbers as (212) 555-0142, others in international format; unparseable as is.</summary>
    public static string Display(string phone)
    {
        try
        {
            var number = Util.Parse(phone, DefaultRegion);
            return Util.Format(number, number.CountryCode == 1 ? PhoneNumberFormat.NATIONAL : PhoneNumberFormat.INTERNATIONAL);
        }
        catch (NumberParseException)
        {
            return phone;
        }
    }

    /// <summary>The message shown when a number can't be read.</summary>
    public static string InvalidMessage(string? region) =>
        (IsSupportedRegion(region) ? region!.ToUpperInvariant() : DefaultRegion) == "US"
            ? "Enter a valid US phone number, e.g. (212) 555-0142, or include the country code, e.g. +44 20 7946 0958."
            : $"Enter a valid phone number for {CountryName(region)}, or include the country code (+...).";

    /// <summary>Countries for the clinic form: United States first, then the rest by name.</summary>
    public static IReadOnlyList<(string Code, string Name)> Countries()
    {
        var list = new List<(string Code, string Name)>();
        foreach (var code in Regions)
        {
            var name = CountryName(code);
            if (name != code) list.Add((code.ToUpperInvariant(), name));
        }

        return list
            .OrderBy(x => x.Code == DefaultRegion ? 0 : 1)
            .ThenBy(x => x.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    private static string CountryName(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return DefaultRegion;
        try { return new RegionInfo(code).EnglishName; }
        catch (ArgumentException) { return code.ToUpperInvariant(); }
    }
}
