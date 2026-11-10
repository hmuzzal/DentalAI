using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DentalAI.Application.Common.Security;

namespace DentalAI.Application.Common.Portal;

/// <summary>Sign-in code limits and portal address rules.</summary>
public static partial class PortalRules
{
    public const int CodeValidMinutes = 10;
    public const int MaxCodeAttempts = 5;
    /// <summary>At most this many codes per contact per <see cref="CodeWindow"/>.</summary>
    public const int MaxCodesPerWindow = 3;
    public static readonly TimeSpan CodeWindow = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(15);

    public const int SlugMinLength = 3;
    public const int SlugMaxLength = 40;

    /// <summary>A random 6-digit code.</summary>
    public static string NewCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    /// <summary>The code's hash, salted with the code row's ID so equal codes don't hash alike.</summary>
    public static string HashCode(Guid codeId, string code) => InviteTokens.Hash($"{codeId:N}:{code.Trim()}");

    /// <summary>Constant-time comparison of two hex hashes.</summary>
    public static bool HashEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));

    /// <summary>Lowercase letters and digits, single dashes between them.</summary>
    public static bool IsValidSlug(string? slug) =>
        slug is { Length: >= SlugMinLength and <= SlugMaxLength } && SlugPattern().IsMatch(slug);

    /// <summary>"Smile Dental & Co." → "smile-dental-co". Falls back to "clinic" when nothing usable is left.</summary>
    public static string SlugFrom(string name)
    {
        var slug = NonSlugCharacters().Replace(name.Trim().ToLowerInvariant(), "-").Trim('-');
        if (slug.Length > 30) slug = slug[..30].Trim('-');
        return slug.Length >= SlugMinLength ? slug : "clinic";
    }

    /// <summary>"Jane D." for the choose-a-patient list.</summary>
    public static string DisplayName(string firstName, string lastName) =>
        string.IsNullOrWhiteSpace(lastName) ? firstName.Trim() : $"{firstName.Trim()} {char.ToUpperInvariant(lastName.Trim()[0])}.";

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlugCharacters();
}
