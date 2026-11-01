using System.Globalization;

namespace DentalAI.Application.Common.Billing;

/// <summary>ISO 4217 currencies: which codes are valid, their decimal places, and the default for a country.</summary>
public static class CurrencyRules
{
    public const string Default = "USD";

    /// <summary>Currencies without the usual 2 decimal places (ISO 4217 minor units).</summary>
    private static readonly Dictionary<string, int> MinorUnitExceptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BIF"] = 0, ["CLP"] = 0, ["DJF"] = 0, ["GNF"] = 0, ["ISK"] = 0, ["JPY"] = 0, ["KMF"] = 0, ["KRW"] = 0,
        ["PYG"] = 0, ["RWF"] = 0, ["UGX"] = 0, ["VND"] = 0, ["VUV"] = 0, ["XAF"] = 0, ["XOF"] = 0, ["XPF"] = 0,
        ["BHD"] = 3, ["IQD"] = 3, ["JOD"] = 3, ["KWD"] = 3, ["LYD"] = 3, ["OMR"] = 3, ["TND"] = 3
    };

    /// <summary>Code → English name, from the cultures .NET knows (covers every currency in current use).</summary>
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Known = new(() =>
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                var region = new RegionInfo(culture.Name);
                if (region.ISOCurrencySymbol.Length == 3 && !map.ContainsKey(region.ISOCurrencySymbol))
                    map[region.ISOCurrencySymbol] = region.CurrencyEnglishName;
            }
            catch (ArgumentException)
            {
                // Neutral or unusual culture without a region.
            }
        }
        return map;
    });

    public static bool IsKnown(string? code) => code is { Length: 3 } && Known.Value.ContainsKey(code);

    public static int MinorUnits(string? code) =>
        code is not null && MinorUnitExceptions.TryGetValue(code, out var units) ? units : 2;

    /// <summary>Rounds to the currency's smallest unit, half away from zero (as on receipts).</summary>
    public static decimal Round(decimal amount, string? currency) =>
        Math.Round(amount, MinorUnits(currency), MidpointRounding.AwayFromZero);

    /// <summary>The currency of a country (ISO 3166 alpha-2), e.g. US → USD, BD → BDT; USD if unknown.</summary>
    public static string ForCountry(string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode)) return Default;
        try
        {
            var symbol = new RegionInfo(countryCode).ISOCurrencySymbol;
            return IsKnown(symbol) ? symbol.ToUpperInvariant() : Default;
        }
        catch (ArgumentException)
        {
            return Default;
        }
    }

    /// <summary>For the settings form: US dollar first, then by name.</summary>
    public static IReadOnlyList<(string Code, string Name)> All() =>
        Known.Value
            .Select(x => (Code: x.Key.ToUpperInvariant(), Name: x.Value))
            .OrderBy(x => x.Code == Default ? 0 : 1)
            .ThenBy(x => x.Name, StringComparer.CurrentCulture)
            .ToList();
}
