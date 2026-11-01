using DentalAI.Application.DTOs.Platform;

namespace DentalAI.Application.Common.Interfaces.Persistence;

/// <summary>Platform-wide aggregates for platform admins. Crosses tenants on purpose, and returns counts only.</summary>
public interface IPlatformRepository
{
    Task<PlatformOverviewDto> GetOverviewAsync(DateTime utcNow, CancellationToken cancellationToken);
}
