using DentalAI.Application.DTOs.Auth;
using DentalAI.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalAI.Api.Controllers;

public sealed class AuthController(
    IAuthService authService,
    ILogger<AuthController> logger) : ApiControllerBase
{
    // There is no self sign-up: platform admins create clinics and invite each clinic's first admin, and clinic
    // admins invite their own staff. People join only through a single-use invite link.

    /// <summary>Who an invite link is for (for the "Set your password" page).</summary>
    [AllowAnonymous]
    [HttpGet("invite")]
    public async Task<IActionResult> GetInvite([FromQuery] string token, CancellationToken cancellationToken) =>
        HandleResult(await authService.GetInviteAsync(token, cancellationToken));

    /// <summary>Sets the password for an invited user and signs them in. The link can't be used again.</summary>
    [AllowAnonymous]
    [HttpPost("accept-invite")]
    public async Task<IActionResult> AcceptInvite([FromBody] AcceptInviteRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.AcceptInviteAsync(request, cancellationToken);
        if (result.Success)
        {
            logger.LogInformation("Invite accepted by {Email}", result.Data?.User.Email);
        }

        return HandleResult(result);
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Login attempt for {Email}", request.Email);
        var result = await authService.LoginAsync(request, cancellationToken);
        if (!result.Success)
        {
            logger.LogWarning("Login failed for {Email}: {Message}", request.Email, result.Message);
        }

        return HandleResult(result);
    }

    [AllowAnonymous]
    [HttpPost("refresh-token")]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.RefreshTokenAsync(request, cancellationToken);
        if (!result.Success)
        {
            logger.LogWarning("Refresh token failed: {Message}", result.Message);
        }

        return HandleResult(result);
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized();
        }

        return HandleResult(await authService.LogoutAsync(userId, cancellationToken));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var result = await authService.GetCurrentUserAsync(cancellationToken);
        return HandleResult(result);
    }
}
