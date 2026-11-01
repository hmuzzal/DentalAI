using DentalAI.Application.Common.Billing;
using DentalAI.Application.Common.Models;
using DentalAI.Application.DTOs.Billing;
using DentalAI.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalAI.Api.Controllers;

/// <summary>Clinic billing settings: currency, invoice numbering, payment terms, tax and tooth numbering.</summary>
[Authorize(Policy = Infrastructure.AuthorizationPolicies.ClinicStaff)]
public sealed class BillingController(BillingService billing) : ApiControllerBase
{
    /// <summary>Every clinic user can read them (they decide how amounts are shown).</summary>
    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(CancellationToken cancellationToken) =>
        HandleResult(await billing.GetSettingsAsync(cancellationToken));

    [Authorize(Policy = Infrastructure.AuthorizationPolicies.ClinicAdminOnly)]
    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateBillingSettingsRequest request, CancellationToken cancellationToken) =>
        HandleResult(await billing.UpdateSettingsAsync(request, cancellationToken));

    /// <summary>ISO 4217 currencies for the settings form (US dollar first).</summary>
    [HttpGet("currencies")]
    public IActionResult GetCurrencies() =>
        HandleResult(Result<IReadOnlyList<CurrencyOptionDto>>.Ok(
            CurrencyRules.All().Select(c => new CurrencyOptionDto(c.Code, c.Name, CurrencyRules.MinorUnits(c.Code))).ToList()));
}
