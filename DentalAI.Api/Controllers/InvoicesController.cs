using DentalAI.Application.DTOs.Billing;
using DentalAI.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalAI.Api.Controllers;

/// <summary>
/// The clinic's invoices and payments. Receptionists create, issue and take payments; refunds and voids are for the
/// clinic's admins (both policies apply to those actions).
/// </summary>
[Authorize(Policy = Infrastructure.AuthorizationPolicies.ClinicStaff)]
public sealed class InvoicesController(BillingService billing) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] InvoiceListQuery query, CancellationToken cancellationToken) =>
        HandleResult(await billing.ListAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        HandleResult(await billing.GetAsync(id, cancellationToken));

    /// <summary>A draft. Issue it to give it a number and make it payable.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateInvoiceRequest request, CancellationToken cancellationToken) =>
        HandleResult(await billing.CreateAsync(request, cancellationToken));

    /// <summary>A draft for a completed appointment: one line for its service at the service's current price.</summary>
    [HttpPost("from-appointment/{appointmentId:guid}")]
    public async Task<IActionResult> CreateFromAppointment(Guid appointmentId, CancellationToken cancellationToken) =>
        HandleResult(await billing.CreateFromAppointmentAsync(appointmentId, cancellationToken));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateDraft(Guid id, [FromBody] UpdateInvoiceRequest request, CancellationToken cancellationToken) =>
        HandleResult(await billing.UpdateDraftAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteDraft(Guid id, CancellationToken cancellationToken) =>
        HandleResult(await billing.DeleteDraftAsync(id, cancellationToken));

    [HttpPost("{id:guid}/issue")]
    public async Task<IActionResult> Issue(Guid id, CancellationToken cancellationToken) =>
        HandleResult(await billing.IssueAsync(id, cancellationToken));

    [HttpPost("{id:guid}/payments")]
    public async Task<IActionResult> RecordPayment(Guid id, [FromBody] RecordPaymentRequest request, CancellationToken cancellationToken) =>
        HandleResult(await billing.RecordPaymentAsync(id, request, cancellationToken));

    [Authorize(Policy = Infrastructure.AuthorizationPolicies.ClinicAdminOnly)]
    [HttpPost("{id:guid}/payments/{paymentId:guid}/refunds")]
    public async Task<IActionResult> Refund(Guid id, Guid paymentId, [FromBody] RefundRequest request, CancellationToken cancellationToken) =>
        HandleResult(await billing.RefundAsync(id, paymentId, request, cancellationToken));

    [Authorize(Policy = Infrastructure.AuthorizationPolicies.ClinicAdminOnly)]
    [HttpPost("{id:guid}/void")]
    public async Task<IActionResult> Void(Guid id, [FromBody] VoidInvoiceRequest request, CancellationToken cancellationToken) =>
        HandleResult(await billing.VoidAsync(id, request, cancellationToken));
}
