using DentalAI.Application.DTOs.Scheduling;
using DentalAI.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalAI.Api.Controllers;

[Authorize(Policy = Infrastructure.AuthorizationPolicies.ClinicStaff)]
public sealed class DoctorSchedulesController(ISchedulingService schedulingService) : ApiControllerBase
{
    [HttpGet("{doctorId:guid}")]
    public async Task<IActionResult> Get(Guid doctorId, CancellationToken cancellationToken) =>
        HandleResult(await schedulingService.GetAvailabilityAsync(doctorId, cancellationToken));

    [HttpPut("{doctorId:guid}")]
    public async Task<IActionResult> Save(Guid doctorId, [FromBody] SaveDoctorAvailabilityRequest request, CancellationToken cancellationToken) =>
        HandleResult(await schedulingService.SaveAvailabilityAsync(doctorId, request, cancellationToken));
}
