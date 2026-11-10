using DentalAI.Domain.Entities;

namespace DentalAI.Application.Common.Interfaces;

public interface IJwtTokenGenerator
{
    string GenerateAccessToken(User user);
    DateTime GetAccessTokenExpiryUtc();

    /// <summary>A patient portal access token (portal audience; not accepted by staff endpoints).</summary>
    (string Token, DateTime ExpiresAtUtc) GeneratePatientAccessToken(Patient patient);
    TimeSpan PatientRefreshTokenLifetime { get; }
}
