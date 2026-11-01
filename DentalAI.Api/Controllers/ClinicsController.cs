using DentalAI.Application.DTOs.Clinics;
using DentalAI.Application.Interfaces;
using DentalAI.Application.DTOs.Staff;
using DentalAI.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalAI.Api.Controllers;

[Authorize(Policy = Infrastructure.AuthorizationPolicies.SuperAdminOnly)]
public sealed class ClinicsController(IClinicService clinicService, StaffService staffService) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken) =>
        HandleResult(await clinicService.ListAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken) =>
        HandleResult(await clinicService.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateClinicDto request, CancellationToken cancellationToken) =>
        HandleResult(await clinicService.CreateAsync(request, cancellationToken));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateClinicDto request, CancellationToken cancellationToken) =>
        HandleResult(await clinicService.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        HandleResult(await clinicService.DeleteAsync(id, cancellationToken));

    // ---- A clinic's staff, for platform admins: onboarding (invite the clinic's admins) and suspending accounts.
    // Accounts only; platform admins still can't see the clinic's patients or appointments.

    [HttpGet("{clinicId:guid}/staff")]
    public async Task<IActionResult> GetStaff(Guid clinicId, CancellationToken cancellationToken) =>
        HandleResult(await staffService.ListAsync(clinicId, cancellationToken));

    /// <summary>Invites one of the clinic's admins (platform admins can only invite admins).</summary>
    [HttpPost("{clinicId:guid}/staff/invite")]
    public async Task<IActionResult> InviteStaff(Guid clinicId, [FromBody] InviteStaffRequest request, CancellationToken cancellationToken) =>
        HandleResult(await staffService.InviteAsync(clinicId, request, StaffActor.PlatformAdmin, cancellationToken));

    [HttpPost("{clinicId:guid}/staff/{userId:guid}/renew-invite")]
    public async Task<IActionResult> RenewStaffInvite(Guid clinicId, Guid userId, CancellationToken cancellationToken) =>
        HandleResult(await staffService.RenewInviteAsync(clinicId, userId, cancellationToken));

    [HttpPost("{clinicId:guid}/staff/{userId:guid}/deactivate")]
    public async Task<IActionResult> DeactivateStaff(Guid clinicId, Guid userId, CancellationToken cancellationToken) =>
        HandleResult(await staffService.SetActiveAsync(clinicId, userId, false, StaffActor.PlatformAdmin, cancellationToken));

    [HttpPost("{clinicId:guid}/staff/{userId:guid}/reactivate")]
    public async Task<IActionResult> ReactivateStaff(Guid clinicId, Guid userId, CancellationToken cancellationToken) =>
        HandleResult(await staffService.SetActiveAsync(clinicId, userId, true, StaffActor.PlatformAdmin, cancellationToken));
}
