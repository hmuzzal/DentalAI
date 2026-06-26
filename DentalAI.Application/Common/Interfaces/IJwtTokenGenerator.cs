using DentalAI.Domain.Entities;

namespace DentalAI.Application.Common.Interfaces;

public interface IJwtTokenGenerator
{
    string GenerateAccessToken(User user);
    DateTime GetAccessTokenExpiryUtc();
}
