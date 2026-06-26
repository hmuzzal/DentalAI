using AutoMapper;
using DentalAI.Application.Common.Interfaces;
using DentalAI.Application.Common.Interfaces.Persistence;
using DentalAI.Application.Common.Models;
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
    public async Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.AdminEmail.Trim().ToLowerInvariant();
        var existingUser = await userRepository.GetByEmailAsync(normalizedEmail, cancellationToken);
        if (existingUser is not null)
        {
            return Result<AuthResponse>.Fail(ResultStatus.Conflict, "A user with this email already exists.");
        }

        var existingClinic = await clinicRepository.GetByNameAsync(request.ClinicName.Trim(), cancellationToken);
        if (existingClinic is not null)
        {
            return Result<AuthResponse>.Fail(ResultStatus.Conflict, "A clinic with this name already exists.");
        }

        var clinic = new Clinic
        {
            Name = request.ClinicName.Trim(),
            Email = request.ClinicEmail?.Trim().ToLowerInvariant() ?? normalizedEmail,
            Phone = request.ClinicPhone?.Trim() ?? string.Empty,
            Address = request.ClinicAddress?.Trim() ?? string.Empty,
            TimeZone = "UTC"
        };

        var refreshToken = refreshTokenService.GenerateRefreshToken();
        var user = new User
        {
            ClinicId = clinic.Id,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = normalizedEmail,
            PasswordHash = passwordHasher.Hash(request.Password),
            RefreshToken = refreshToken.Token,
            RefreshTokenExpiry = refreshToken.ExpiresAtUtc,
            Role = UserRole.ClinicAdmin,
            IsActive = true
        };

        await clinicRepository.AddAsync(clinic, cancellationToken);
        await userRepository.AddAsync(user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<AuthResponse>.Ok(BuildAuthResponse(user), "Clinic registered successfully.");
    }

    public async Task<Result<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByEmailAsync(request.Email.Trim().ToLowerInvariant(), cancellationToken);
        if (user is null || !user.IsActive || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            return Result<AuthResponse>.Fail(ResultStatus.Unauthorized, "Invalid email or password.");
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
            || user.RefreshTokenExpiry.Value <= dateTimeProvider.UtcNow)
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

        return Result<UserProfileDto>.Ok(mapper.Map<UserProfileDto>(user));
    }

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
