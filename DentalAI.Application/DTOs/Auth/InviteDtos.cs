using DentalAI.Domain.Enums;

namespace DentalAI.Application.DTOs.Auth;

/// <summary>GET /auth/invite?token=... : who the invite is for, shown on the "Set your password" page.</summary>
public sealed record InviteDetailsDto(
    string FirstName,
    string LastName,
    string Email,
    string ClinicName,
    UserRole Role,
    DateTime ExpiresAtUtc);

public sealed class AcceptInviteRequest
{
    public string Token { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
