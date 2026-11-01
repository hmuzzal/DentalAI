using DentalAI.Application.DTOs.Appointments;
using DentalAI.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalAI.Api.Controllers;

[Authorize(Policy = Infrastructure.AuthorizationPolicies.ReceptionistAccess)]
public sealed class AppointmentsController(
    IAppointmentService appointmentService,
    ISchedulingService schedulingService,
    ILogger<AppointmentsController> logger) : ApiControllerBase
{
    [HttpGet("available-slots")]
    public async Task<IActionResult> GetAvailableSlots(
        [FromQuery] Guid doctorId,
        [FromQuery] DateOnly date,
        [FromQuery] Guid serviceId,
        CancellationToken cancellationToken) =>
        HandleResult(await schedulingService.GetAvailableSlotsAsync(doctorId, date, serviceId, cancellationToken));

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken) =>
        HandleResult(await appointmentService.ListAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken) =>
        HandleResult(await appointmentService.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAppointmentRequest request, CancellationToken cancellationToken)
    {
        var result = await appointmentService.CreateAppointmentAsync(request, cancellationToken);
        if (result.Success)
        {
            logger.LogInformation("Appointment created for doctor {DoctorId} at {StartTime}", request.DoctorId, request.StartTime);
        }

        return HandleResult(result);
    }

    [HttpPost("{id:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid id, [FromBody] UpdateAppointmentStatusRequest? request, CancellationToken cancellationToken) =>
        HandleStatusResult("confirmed", id, await appointmentService.ConfirmAsync(id, request ?? new UpdateAppointmentStatusRequest(), cancellationToken));

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] UpdateAppointmentStatusRequest? request, CancellationToken cancellationToken) =>
        HandleStatusResult("cancelled", id, await appointmentService.CancelAsync(id, request ?? new UpdateAppointmentStatusRequest(), cancellationToken));

    [HttpPost("{id:guid}/complete")]
    public async Task<IActionResult> Complete(Guid id, [FromBody] UpdateAppointmentStatusRequest? request, CancellationToken cancellationToken) =>
        HandleStatusResult("completed", id, await appointmentService.CompleteAsync(id, request ?? new UpdateAppointmentStatusRequest(), cancellationToken));

    [HttpPost("{id:guid}/noshow")]
    public async Task<IActionResult> NoShow(Guid id, [FromBody] UpdateAppointmentStatusRequest? request, CancellationToken cancellationToken) =>
        HandleStatusResult("marked no-show", id, await appointmentService.NoShowAsync(id, request ?? new UpdateAppointmentStatusRequest(), cancellationToken));

    private IActionResult HandleStatusResult(string action, Guid appointmentId, DentalAI.Application.Common.Models.Result<AppointmentDto> result)
    {
        if (result.Success)
        {
            logger.LogInformation("Appointment {AppointmentId} {Action}", appointmentId, action);
        }

        return HandleResult(result);
    }
}
