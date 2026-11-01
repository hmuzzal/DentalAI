using DentalAI.Application.Common.Interfaces;
using DentalAI.Application.Common.Interfaces.Persistence;
using DentalAI.Application.Common.Models;
using DentalAI.Application.DTOs.Platform;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalAI.Api.Controllers;

/// <summary>Platform admins' view of the platform: counts per clinic, no clinic data.</summary>
[Authorize(Policy = Infrastructure.AuthorizationPolicies.SuperAdminOnly)]
public sealed class PlatformController(IPlatformRepository platformRepository, IDateTimeProvider dateTimeProvider) : ApiControllerBase
{
    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview(CancellationToken cancellationToken) =>
        HandleResult(Result<PlatformOverviewDto>.Ok(await platformRepository.GetOverviewAsync(dateTimeProvider.UtcNow, cancellationToken)));
}
