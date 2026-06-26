using DentalAI.Application.Common.Models;
using DentalAI.Application.DTOs.Auth;
using DentalAI.Application.DTOs.Users;

namespace DentalAI.Application.Interfaces;

public interface IAuthService
{
    Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);
    Task<Result<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<Result<AuthResponse>> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken);
    Task<Result> LogoutAsync(Guid userId, CancellationToken cancellationToken);
    Task<Result<UserProfileDto>> GetCurrentUserAsync(CancellationToken cancellationToken);
}
