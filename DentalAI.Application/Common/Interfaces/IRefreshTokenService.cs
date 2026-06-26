namespace DentalAI.Application.Common.Interfaces;

public interface IRefreshTokenService
{
    (string Token, DateTime ExpiresAtUtc) GenerateRefreshToken();
}
