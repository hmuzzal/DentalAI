using DentalAI.Domain.Common;
using DentalAI.Domain.Enums;

namespace DentalAI.Domain.Entities;

public sealed class User : BaseEntity, ITenantEntity
{
    public Guid? ClinicId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    /// <summary>Empty until the user accepts their invite and sets a password.</summary>
    public string PasswordHash { get; set; } = string.Empty;
    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiry { get; set; }
    public UserRole Role { get; set; } = UserRole.Receptionist;
    public bool IsActive { get; set; } = true;

    /// <summary>SHA-256 (hex) of the single-use invite token. The token itself is only ever in the invite link.</summary>
    public string? InviteTokenHash { get; set; }
    public DateTime? InviteExpiresAtUtc { get; set; }

    public Clinic? Clinic { get; set; }
}
