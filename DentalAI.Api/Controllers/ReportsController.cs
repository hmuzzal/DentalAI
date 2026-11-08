using DentalAI.Application.DTOs.Reports;
using DentalAI.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalAI.Api.Controllers;

/// <summary>Practice reports for the signed-in clinic. Dates are clinic-local, yyyy-MM-dd.</summary>
[Authorize(Policy = Infrastructure.AuthorizationPolicies.ClinicStaff)]
public sealed class ReportsController(ReportService reports) : ApiControllerBase
{
    /// <summary>Production, collections, visits and patients for a range, with the previous range for comparison.</summary>
    [HttpGet("practice")]
    public async Task<IActionResult> GetPractice([FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        HandleResult(await reports.GetPracticeReportAsync(query, cancellationToken));

    /// <summary>What patients owe today, by days past due.</summary>
    [HttpGet("receivables")]
    public async Task<IActionResult> GetReceivables(CancellationToken cancellationToken) =>
        HandleResult(await reports.GetReceivablesAsync(cancellationToken));
}
