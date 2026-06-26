using DentalAI.Domain.Common;

namespace DentalAI.Domain.Entities;

public sealed class RefreshToken : BaseEntity, ITenantEntity
{
    public Guid? ClinicId { get; set; }
    public Guid UserId { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? ReplacedByToken { get; set; }

    public User? User { get; set; }
}
