using System.Security.Claims;
using DentalAI.Application.Common.Interfaces;
using DentalAI.Domain.Enums;

namespace DentalAI.Api.Infrastructure;

public sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    public Guid? UserId => TryParseGuid(ClaimTypes.NameIdentifier);
    public Guid? ClinicId => TryParseGuid("clinicId");
    public string? Email => httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Email);
    public bool IsAuthenticated => httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;
    public bool IsSuperAdmin => IsInRole(UserRole.SuperAdmin);

    public bool IsInRole(UserRole role) => httpContextAccessor.HttpContext?.User.IsInRole(role.ToString()) ?? false;

    /// <summary>True only for the user's own clinic. Platform admins have no clinic, so this is always false for them.</summary>
    public bool CanAccessClinic(Guid? clinicId) => clinicId.HasValue && ClinicId == clinicId;

    private Guid? TryParseGuid(string claimType)
    {
        var value = httpContextAccessor.HttpContext?.User.FindFirstValue(claimType);
        return Guid.TryParse(value, out var parsed) ? parsed : null;
    }
}
