using DentalAI.Application.DTOs.Auth;
using DentalAI.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalAI.Api.Controllers;

public sealed class AuthController(
    IAuthService authService,
    ILogger<AuthController> logger) : ApiControllerBase
{
    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Registration attempt for clinic {ClinicName} with admin {Email}", request.ClinicName, request.AdminEmail);
        var result = await authService.RegisterAsync(request, cancellationToken);
        if (!result.Success)
        {
            logger.LogWarning("Registration failed for {Email}: {Message}", request.AdminEmail, result.Message);
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
