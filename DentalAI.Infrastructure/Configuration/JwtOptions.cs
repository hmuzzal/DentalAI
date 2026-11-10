namespace DentalAI.Infrastructure.Configuration;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public int AccessTokenExpirationMinutes { get; set; } = 60;
    public int RefreshTokenExpirationDays { get; set; } = 7;

    /// <summary>Audience of patient portal tokens. Must differ from <see cref="Audience"/>.</summary>
    public string PortalAudience { get; set; } = "DentalAI.Portal";
    public int PortalAccessTokenExpirationMinutes { get; set; } = 30;
    public int PortalRefreshTokenExpirationDays { get; set; } = 30;
}
