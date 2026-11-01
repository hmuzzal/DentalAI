using DentalAI.Application.Common.Interfaces;
using DentalAI.Application.Common.Models;
using DentalAI.Application.DTOs.Staff;
using DentalAI.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalAI.Api.Controllers;

/// <summary>A clinic admin manages their own clinic's staff. Receptionists can't.</summary>
[Authorize(Policy = Infrastructure.AuthorizationPolicies.ClinicAdminOnly)]
public sealed class StaffController(StaffService staffService, ICurrentUserService currentUserService) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken) =>
        await WithClinic(clinicId => staffService.ListAsync(clinicId, cancellationToken));

    [HttpPost("invite")]
    public async Task<IActionResult> Invite([FromBody] InviteStaffRequest request, CancellationToken cancellationToken) =>
        await WithClinic(clinicId => staffService.InviteAsync(clinicId, request, StaffActor.ClinicAdmin, cancellationToken));

    [HttpPost("{userId:guid}/renew-invite")]
    public async Task<IActionResult> RenewInvite(Guid userId, CancellationToken cancellationToken) =>
        await WithClinic(clinicId => staffService.RenewInviteAsync(clinicId, userId, cancellationToken));

    [HttpPut("{userId:guid}/role")]
    public async Task<IActionResult> ChangeRole(Guid userId, [FromBody] ChangeStaffRoleRequest request, CancellationToken cancellationToken) =>
        await WithClinic(clinicId => staffService.ChangeRoleAsync(clinicId, userId, request.Role, cancellationToken));

    [HttpPost("{userId:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid userId, CancellationToken cancellationToken) =>
        await WithClinic(clinicId => staffService.SetActiveAsync(clinicId, userId, false, StaffActor.ClinicAdmin, cancellationToken));

    [HttpPost("{userId:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid userId, CancellationToken cancellationToken) =>
        await WithClinic(clinicId => staffService.SetActiveAsync(clinicId, userId, true, StaffActor.ClinicAdmin, cancellationToken));

    private async Task<IActionResult> WithClinic<T>(Func<Guid, Task<Result<T>>> action) =>
        currentUserService.ClinicId is { } clinicId
            ? HandleResult(await action(clinicId))
            : HandleResult(Result<T>.Fail(ResultStatus.Forbidden, "Clinic context is required."));
}
