using System.Text.RegularExpressions;

namespace DentalAI.Application.Common.People;

/// <summary>
/// "Dr. Sarah Ahmed" for patients. Staff sometimes type the title into the name itself (first name "Dr."), so any
/// title already there is dropped before ours is added: never "Dr. Dr. Sarah Ahmed".
/// </summary>
public static partial class DoctorNames
{
    public static string WithTitle(string? firstName, string? lastName) =>
        WithTitle($"{firstName?.Trim()} {lastName?.Trim()}");

    /// <summary>"Sarah Ahmed", "Dr. Sarah Ahmed" or "Doctor Ahmed" all become "Dr. ..."; an empty name stays empty.</summary>
    public static string WithTitle(string? name)
    {
        var bare = WithoutTitle(name);
        return bare.Length == 0 ? string.Empty : $"Dr. {bare}";
    }

    public static string WithoutTitle(string? name)
    {
        var text = Spaces().Replace(name ?? string.Empty, " ").Trim();
        while (Title().Match(text) is { Success: true } match) text = text[match.Length..].TrimStart();
        return text;
    }

    [GeneratedRegex(@"^(?:dr\b\.?|doctor\b)\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Title();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
