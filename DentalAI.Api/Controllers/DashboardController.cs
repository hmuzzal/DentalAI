using DentalAI.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalAI.Api.Controllers;

[Authorize(Policy = Infrastructure.AuthorizationPolicies.ReceptionistAccess)]
public sealed class DashboardController(IDashboardService dashboardService) : ApiControllerBase
{
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken cancellationToken) =>
        HandleResult(await dashboardService.GetStatsAsync(cancellationToken));
}
