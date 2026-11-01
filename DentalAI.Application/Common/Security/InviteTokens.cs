using System.Security.Cryptography;
using System.Text;

namespace DentalAI.Application.Common.Security;

/// <summary>
/// Single-use invite tokens. The raw token goes only into the invite link; the database keeps its SHA-256, so a
/// leaked database can't be used to accept invites.
/// </summary>
public static class InviteTokens
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    /// <summary>A new URL-safe token (256 bits) and the hash to store.</summary>
    public static (string Token, string Hash) Create()
    {
        var token = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        return (token, Hash(token));
    }

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
