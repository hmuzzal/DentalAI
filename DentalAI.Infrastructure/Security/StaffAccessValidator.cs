using System.Security.Claims;
using DentalAI.Application.Common.Interfaces;
using DentalAI.Domain.Enums;
using DentalAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace DentalAI.Infrastructure.Security;

/// <summary>
/// Rejects a staff access token whose account was deactivated or deleted, whose clinic was deleted, or whose role
/// or clinic changed since it was issued. Without this a token keeps working until it expires (up to an hour).
/// The account is read at most once per <see cref="CacheFor"/> per server; changes made through the app clear the
/// entry straight away (<see cref="Forget"/>), so the delay only applies to other servers or direct DB edits.
/// </summary>
public sealed class StaffAccessValidator(IMemoryCache cache, IServiceScopeFactory scopeFactory) : IStaffAccessCache
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    private sealed record Account(bool CanSignIn, UserRole Role, Guid? ClinicId);

    public void Forget(Guid userId) => cache.Remove(Key(userId));

    /// <summary>Null when the token's holder may still act; otherwise the reason it can't.</summary>
    public async Task<string?> CheckAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
            return "The token has no user.";

        var account = await cache.GetOrCreateAsync(Key(userId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            // No signed-in user yet at this point, so bypass the tenant filter and check soft deletes by hand.
            return await db.Users.IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.Id == userId && !u.IsDeleted)
                .Select(u => new Account(
                    u.IsActive && (u.Clinic == null || !u.Clinic.IsDeleted),
                    u.Role,
                    u.ClinicId))
                .FirstOrDefaultAsync(cancellationToken);
        });

        if (account is null) return "The account no longer exists.";
        if (!account.CanSignIn) return "The account is deactivated or its clinic was deleted.";
        if (!principal.IsInRole(account.Role.ToString())) return "The account's role has changed.";

        var tokenClinic = Guid.TryParse(principal.FindFirst("clinicId")?.Value, out var c) ? c : (Guid?)null;
        return tokenClinic == account.ClinicId ? null : "The account's clinic has changed.";
    }

    private static string Key(Guid userId) => $"staff-access:{userId:N}";
}
