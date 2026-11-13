using DentalAI.Api.Infrastructure;
using DentalAI.Application.DTOs.Portal;
using DentalAI.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DentalAI.Api.Controllers;

/// <summary>
/// The patient portal. Sign-in endpoints are anonymous and rate-limited; everything else needs a patient token
/// (portal audience), which staff endpoints never accept. A patient only ever sees their own records.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.PortalPatient)]
public sealed class PortalController(PortalService portal, ReceptionistService assistant) : ApiControllerBase
{
    // ---- Public ----

    [AllowAnonymous]
    [HttpGet("clinics/{slug}")]
    public async Task<IActionResult> GetClinic(string slug, CancellationToken cancellationToken) =>
        HandleResult(await portal.GetClinicAsync(slug, cancellationToken));

    [AllowAnonymous]
    [EnableRateLimiting(PortalRateLimits.SignIn)]
    [HttpPost("clinics/{slug}/auth/start")]
    public async Task<IActionResult> Start(string slug, [FromBody] PortalStartRequest request, CancellationToken cancellationToken) =>
        HandleResult(await portal.StartSignInAsync(slug, request, cancellationToken));

    [AllowAnonymous]
    [EnableRateLimiting(PortalRateLimits.SignIn)]
    [HttpPost("clinics/{slug}/auth/verify")]
    public async Task<IActionResult> Verify(string slug, [FromBody] PortalVerifyRequest request, CancellationToken cancellationToken) =>
        HandleResult(await portal.VerifyCodeAsync(slug, request, cancellationToken));

    [AllowAnonymous]
    [EnableRateLimiting(PortalRateLimits.SignIn)]
    [HttpPost("clinics/{slug}/auth/select")]
    public async Task<IActionResult> Select(string slug, [FromBody] PortalSelectRequest request, CancellationToken cancellationToken) =>
        HandleResult(await portal.SelectPatientAsync(slug, request, cancellationToken));

    [AllowAnonymous]
    [EnableRateLimiting(PortalRateLimits.SignIn)]
    [HttpPost("clinics/{slug}/auth/register")]
    public async Task<IActionResult> Register(string slug, [FromBody] PortalRegisterRequest request, CancellationToken cancellationToken) =>
        HandleResult(await portal.RegisterAsync(slug, request, cancellationToken));

    [AllowAnonymous]
    [EnableRateLimiting(PortalRateLimits.SignIn)]
    [HttpPost("auth/refresh")]
    public async Task<IActionResult> Refresh([FromBody] PortalRefreshRequest request, CancellationToken cancellationToken) =>
        HandleResult(await portal.RefreshAsync(request, cancellationToken));

    [AllowAnonymous]
    [HttpPost("auth/logout")]
    public async Task<IActionResult> Logout([FromBody] PortalRefreshRequest request, CancellationToken cancellationToken) =>
        HandleResult(await portal.SignOutAsync(request, cancellationToken));

    // ---- Signed-in patient ----

    [HttpGet("me")]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken) =>
        HandleResult(await portal.GetProfileAsync(cancellationToken));

    [HttpPut("me")]
    public async Task<IActionResult> UpdateProfile([FromBody] PortalUpdateProfileRequest request, CancellationToken cancellationToken) =>
        HandleResult(await portal.UpdateProfileAsync(request, cancellationToken));

    [HttpGet("services")]
    public async Task<IActionResult> GetServices(CancellationToken cancellationToken) =>
        HandleResult(await portal.ListServicesAsync(cancellationToken));

    [HttpGet("doctors")]
    public async Task<IActionResult> GetDoctors(CancellationToken cancellationToken) =>
        HandleResult(await portal.ListDoctorsAsync(cancellationToken));

    /// <summary>Open times on a clinic-local day. <c>rescheduleAppointmentId</c>: one of your appointments being moved.</summary>
    [HttpGet("slots")]
    public async Task<IActionResult> GetSlots([FromQuery] Guid doctorId, [FromQuery] Guid serviceId, [FromQuery] DateOnly date,
        [FromQuery] Guid? rescheduleAppointmentId, CancellationToken cancellationToken) =>
        HandleResult(await portal.GetSlotsAsync(doctorId, serviceId, date, rescheduleAppointmentId, cancellationToken));

    [HttpGet("appointments")]
    public async Task<IActionResult> GetAppointments(CancellationToken cancellationToken) =>
        HandleResult(await portal.ListAppointmentsAsync(cancellationToken));

    [HttpPost("appointments")]
    public async Task<IActionResult> Book([FromBody] PortalBookRequest request, CancellationToken cancellationToken) =>
        HandleResult(await portal.BookAsync(request, cancellationToken));

    [HttpPost("appointments/{id:guid}/reschedule")]
    public async Task<IActionResult> Reschedule(Guid id, [FromBody] PortalRescheduleRequest request, CancellationToken cancellationToken) =>
        HandleResult(await portal.RescheduleAsync(id, request, cancellationToken));

    [HttpPost("appointments/{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken) =>
        HandleResult(await portal.CancelAsync(id, cancellationToken));

    [HttpGet("invoices")]
    public async Task<IActionResult> GetInvoices([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        HandleResult(await portal.ListInvoicesAsync(page, pageSize, cancellationToken));

    [HttpGet("invoices/{id:guid}")]
    public async Task<IActionResult> GetInvoice(Guid id, CancellationToken cancellationToken) =>
        HandleResult(await portal.GetInvoiceAsync(id, cancellationToken));

    // ---- Signed-in patient: AI assistant ----

    /// <summary>Whether the assistant is on (the portal hides it otherwise).</summary>
    [HttpGet("assistant")]
    public IActionResult GetAssistant() => HandleResult(assistant.GetStatus());

    /// <summary>One patient message; the reply comes back when the assistant has finished (it may book, move or cancel).</summary>
    [EnableRateLimiting(PortalRateLimits.Assistant)]
    [HttpPost("assistant/messages")]
    public async Task<IActionResult> SendAssistantMessage([FromBody] AssistantMessageRequest request, CancellationToken cancellationToken) =>
        HandleResult(await assistant.SendAsync(request, cancellationToken));
}
