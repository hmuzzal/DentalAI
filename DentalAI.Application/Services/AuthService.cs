using AutoMapper;
using DentalAI.Application.Common.Interfaces;
using DentalAI.Application.Common.Interfaces.Persistence;
using DentalAI.Application.Common.Models;
using DentalAI.Application.Common.Security;
using DentalAI.Application.DTOs.Auth;
using DentalAI.Application.DTOs.Users;
using DentalAI.Application.Interfaces;
using DentalAI.Domain.Entities;
using DentalAI.Domain.Enums;

namespace DentalAI.Application.Services;

public sealed class AuthService(
    IClinicRepository clinicRepository,
    IUserRepository userRepository,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    IJwtTokenGenerator jwtTokenGenerator,
    IRefreshTokenService refreshTokenService,
    ICurrentUserService currentUserService,
    IDateTimeProvider dateTimeProvider,
    IMapper mapper) : IAuthService
{
    public async Task<Result<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByEmailAsync(request.Email.Trim().ToLowerInvariant(), cancellationToken);
        // Invited users have no password until they accept their invite.
        if (user is null || !user.IsActive || string.IsNullOrEmpty(user.PasswordHash)
            || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            return Result<AuthResponse>.Fail(ResultStatus.Unauthorized, "Invalid email or password.");
        }

        if (IsClinicClosed(user))
        {
            return Result<AuthResponse>.Fail(ResultStatus.Unauthorized, ClinicClosedMessage);
        }

        SetRefreshToken(user);
        userRepository.Update(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<AuthResponse>.Ok(BuildAuthResponse(user), "Login successful.");
    }

    public async Task<Result<AuthResponse>> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByRefreshTokenAsync(request.RefreshToken, cancellationToken);
        if (user is null
            || !user.IsActive
            || string.IsNullOrWhiteSpace(user.RefreshToken)
            || user.RefreshToken != request.RefreshToken
            || !user.RefreshTokenExpiry.HasValue
            || user.RefreshTokenExpiry.Value <= dateTimeProvider.UtcNow
            || IsClinicClosed(user))
        {
            return Result<AuthResponse>.Fail(ResultStatus.Unauthorized, "Refresh token is invalid or expired.");
        }

        SetRefreshToken(user);
        userRepository.Update(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<AuthResponse>.Ok(BuildAuthResponse(user), "Token refreshed successfully.");
    }

    public async Task<Result> LogoutAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Fail(ResultStatus.NotFound, "User was not found.");
        }

        user.RefreshToken = null;
        user.RefreshTokenExpiry = null;
        userRepository.Update(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Ok("Logout successful.");
    }

    public async Task<Result<UserProfileDto>> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        if (!currentUserService.UserId.HasValue)
        {
            return Result<UserProfileDto>.Fail(ResultStatus.Unauthorized, "User is not authenticated.");
        }

        var user = await userRepository.GetByIdAsync(currentUserService.UserId.Value, cancellationToken);
        if (user is null)
        {
            return Result<UserProfileDto>.Fail(ResultStatus.NotFound, "User was not found.");
        }

        // A deactivated user or a closed clinic ends the session on the next page load, not when the token expires.
        if (!user.IsActive || IsClinicClosed(user))
        {
            return Result<UserProfileDto>.Fail(ResultStatus.Unauthorized,
                user.IsActive ? ClinicClosedMessage : "Your account has been deactivated.");
        }

        return Result<UserProfileDto>.Ok(mapper.Map<UserProfileDto>(user));
    }

    /// <summary>Who an invite link is for. Unknown, used and expired links all get the same answer.</summary>
    public async Task<Result<InviteDetailsDto>> GetInviteAsync(string token, CancellationToken cancellationToken)
    {
        var user = await FindOpenInviteAsync(token, cancellationToken);
        return user is null
            ? Result<InviteDetailsDto>.Fail(ResultStatus.NotFound, InvalidInviteMessage)
            : Result<InviteDetailsDto>.Ok(new InviteDetailsDto(user.FirstName, user.LastName, user.Email,
                user.Clinic?.Name ?? string.Empty, user.Role, user.InviteExpiresAtUtc!.Value));
    }

    /// <summary>Sets the invited user's password, uses up the link, and signs them in.</summary>
    public async Task<Result<AuthResponse>> AcceptInviteAsync(AcceptInviteRequest request, CancellationToken cancellationToken)
    {
        var user = await FindOpenInviteAsync(request.Token, cancellationToken);
        if (user is null)
        {
            return Result<AuthResponse>.Fail(ResultStatus.NotFound, InvalidInviteMessage);
        }

        user.PasswordHash = passwordHasher.Hash(request.Password);
        user.InviteTokenHash = null;
        user.InviteExpiresAtUtc = null;
        SetRefreshToken(user);
        userRepository.Update(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<AuthResponse>.Ok(BuildAuthResponse(user), "Your password is set. Welcome to DentalAI.");
    }

    private const string InvalidInviteMessage =
        "This invite link is invalid or has expired. Ask your clinic's admin for a new one.";
    private const string ClinicClosedMessage = "This clinic's account has been closed.";

    /// <summary>An invite that exists, hasn't been used or expired, for an active user of an open clinic.</summary>
    private async Task<User?> FindOpenInviteAsync(string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var user = await userRepository.GetByInviteTokenHashAsync(InviteTokens.Hash(token.Trim()), cancellationToken);
        return user is { IsActive: true, InviteExpiresAtUtc: not null }
               && user.InviteExpiresAtUtc > dateTimeProvider.UtcNow
               && string.IsNullOrEmpty(user.PasswordHash)
               && !IsClinicClosed(user)
            ? user
            : null;
    }

    /// <summary>Staff of a deleted clinic can't sign in. Platform admins have no clinic.</summary>
    private static bool IsClinicClosed(User user) =>
        user.ClinicId.HasValue && (user.Clinic is null || user.Clinic.IsDeleted);

    private AuthResponse BuildAuthResponse(User user) => new()
    {
        AccessToken = jwtTokenGenerator.GenerateAccessToken(user),
        RefreshToken = user.RefreshToken ?? string.Empty,
        ExpiresAtUtc = jwtTokenGenerator.GetAccessTokenExpiryUtc(),
        User = mapper.Map<UserProfileDto>(user)
    };

    private void SetRefreshToken(User user)
    {
        var refreshToken = refreshTokenService.GenerateRefreshToken();
        user.RefreshToken = refreshToken.Token;
        user.RefreshTokenExpiry = refreshToken.ExpiresAtUtc;
    }
}
