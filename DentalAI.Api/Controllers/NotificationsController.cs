using DentalAI.Application.DTOs.Notifications;
using DentalAI.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalAI.Api.Controllers;

/// <summary>The clinic's reminder settings and its message log.</summary>
[Authorize(Policy = Infrastructure.AuthorizationPolicies.ClinicStaff)]
public sealed class NotificationsController(NotificationService notifications) : ApiControllerBase
{
    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(CancellationToken cancellationToken) =>
        HandleResult(await notifications.GetSettingsAsync(cancellationToken));

    [Authorize(Policy = Infrastructure.AuthorizationPolicies.ClinicAdminOnly)]
    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateNotificationSettingsRequest request, CancellationToken cancellationToken) =>
        HandleResult(await notifications.UpdateSettingsAsync(request, cancellationToken));

    /// <summary>Every email and text the clinic has sent or will send, newest first.</summary>
    [HttpGet("messages")]
    public async Task<IActionResult> GetMessages([FromQuery] MessageListQuery query, CancellationToken cancellationToken) =>
        HandleResult(await notifications.ListAsync(query, cancellationToken));

    [Authorize(Policy = Infrastructure.AuthorizationPolicies.ClinicAdminOnly)]
    [HttpPost("test")]
    public async Task<IActionResult> SendTest([FromBody] SendTestMessageRequest request, CancellationToken cancellationToken) =>
        HandleResult(await notifications.SendTestAsync(request, cancellationToken));
}
