using DentalAI.Application.DTOs.Doctors;
using DentalAI.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalAI.Api.Controllers;

[Authorize(Policy = Infrastructure.AuthorizationPolicies.ReceptionistAccess)]
public sealed class DoctorsController(IDoctorService doctorService) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid? clinicId, CancellationToken cancellationToken) =>
        HandleResult(await doctorService.ListAsync(clinicId, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken) =>
        HandleResult(await doctorService.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateDoctorDto request, CancellationToken cancellationToken) =>
        HandleResult(await doctorService.CreateAsync(request, cancellationToken));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDoctorDto request, CancellationToken cancellationToken) =>
        HandleResult(await doctorService.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        HandleResult(await doctorService.DeleteAsync(id, cancellationToken));
}
