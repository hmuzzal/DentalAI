using System.Security.Cryptography;
using DentalAI.Application.Common.Interfaces;
using DentalAI.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace DentalAI.Infrastructure.Security;

public sealed class RefreshTokenService(IOptions<JwtOptions> jwtOptions) : IRefreshTokenService
{
    private readonly JwtOptions _options = jwtOptions.Value;

    public (string Token, DateTime ExpiresAtUtc) GenerateRefreshToken()
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(64);
        return (Convert.ToBase64String(tokenBytes), DateTime.UtcNow.AddDays(_options.RefreshTokenExpirationDays));
    }
}
