using DentalAI.Application.DTOs.Patients;
using DentalAI.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalAI.Api.Controllers;

[Authorize(Policy = Infrastructure.AuthorizationPolicies.ClinicStaff)]
public sealed class PatientsController(IPatientService patientService) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken) =>
        HandleResult(await patientService.ListAsync(cancellationToken));

    /// <summary>Paged patient search by name, phone or email.</summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] PatientSearchQuery query, CancellationToken cancellationToken) =>
        HandleResult(await patientService.SearchAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken) =>
        HandleResult(await patientService.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UpsertPatientRequest request, CancellationToken cancellationToken) =>
        HandleResult(await patientService.CreateAsync(request, cancellationToken));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpsertPatientRequest request, CancellationToken cancellationToken) =>
        HandleResult(await patientService.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        HandleResult(await patientService.DeleteAsync(id, cancellationToken));
}
