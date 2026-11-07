using System.Security.Cryptography;
using System.Text;

namespace DentalAI.Infrastructure.Notifications;

/// <summary>
/// Checks the X-Twilio-Signature header on Twilio webhooks: base64(HMAC-SHA1(authToken, url + each form key and value,
/// keys sorted)). Without this, anyone could post a fake "STOP" or "C" for any number.
/// </summary>
public static class TwilioSignature
{
    public static string Compute(string authToken, string url, IEnumerable<KeyValuePair<string, string>> form)
    {
        var data = new StringBuilder(url);
        foreach (var (key, value) in form.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            data.Append(key).Append(value);
        }
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(authToken));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(data.ToString())));
    }

    public static bool IsValid(string authToken, string url, IEnumerable<KeyValuePair<string, string>> form, string? signature)
    {
        if (string.IsNullOrEmpty(authToken) || string.IsNullOrEmpty(signature)) return false;
        var expected = Encoding.UTF8.GetBytes(Compute(authToken, url, form));
        var actual = Encoding.UTF8.GetBytes(signature);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
