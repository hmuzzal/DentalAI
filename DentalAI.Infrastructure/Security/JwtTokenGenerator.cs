using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DentalAI.Application.Common.Interfaces;
using DentalAI.Application.Common.Portal;
using DentalAI.Domain.Entities;
using DentalAI.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DentalAI.Infrastructure.Security;

public sealed class JwtTokenGenerator(IOptions<JwtOptions> jwtOptions) : IJwtTokenGenerator
{
    private readonly JwtOptions _options = jwtOptions.Value;

    public string GenerateAccessToken(User user)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, $"{user.FirstName} {user.LastName}".Trim()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role.ToString())
        };

        if (user.ClinicId.HasValue)
        {
            claims.Add(new Claim("clinicId", user.ClinicId.Value.ToString()));
        }

        return Write(claims, _options.Audience, GetAccessTokenExpiryUtc());
    }

    public DateTime GetAccessTokenExpiryUtc() => DateTime.UtcNow.AddMinutes(_options.AccessTokenExpirationMinutes);

    /// <summary>
    /// No NameIdentifier or email claim: a patient is never mistaken for a staff user. The portal audience keeps the
    /// token out of every staff endpoint.
    /// </summary>
    public (string Token, DateTime ExpiresAtUtc) GeneratePatientAccessToken(Patient patient)
    {
        // Real time, not IDateTimeProvider: the bearer handler validates expiry against the real clock.
        var expires = DateTime.UtcNow.AddMinutes(_options.PortalAccessTokenExpirationMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, $"patient:{patient.Id:N}"),
            new(PortalClaims.PatientId, patient.Id.ToString()),
            new(PortalClaims.ClinicId, patient.ClinicId!.Value.ToString()),
            new(ClaimTypes.Name, $"{patient.FirstName} {patient.LastName}".Trim()),
            new(ClaimTypes.Role, PortalClaims.PatientRole)
        };
        return (Write(claims, _options.PortalAudience, expires), expires);
    }

    public TimeSpan PatientRefreshTokenLifetime => TimeSpan.FromDays(_options.PortalRefreshTokenExpirationDays);

    private string Write(IEnumerable<Claim> claims, string audience, DateTime expires)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SecretKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: audience,
            claims: claims,
            expires: expires,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
