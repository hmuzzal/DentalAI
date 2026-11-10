using DentalAI.Application.DTOs.Portal;
using DentalAI.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalAI.Api.Controllers;

/// <summary>The clinic's patient portal: on/off, its address and booking rules. Clinic admins only.</summary>
[Authorize(Policy = Infrastructure.AuthorizationPolicies.ClinicAdminOnly)]
public sealed class PortalSettingsController(PortalService portal) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        HandleResult(await portal.GetSettingsAsync(cancellationToken));

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdatePortalSettingsRequest request, CancellationToken cancellationToken) =>
        HandleResult(await portal.UpdateSettingsAsync(request, cancellationToken));
}
