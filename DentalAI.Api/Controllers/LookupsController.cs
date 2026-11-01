using DentalAI.Application.Common.Interfaces;
using DentalAI.Application.Common.Models;
using DentalAI.Application.DTOs.Lookups;
using DentalAI.Application.Services;
using DentalAI.Application.Validators;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalAI.Api.Controllers;

[Authorize]
public sealed class LookupsController(IDateTimeProvider dateTimeProvider) : ApiControllerBase
{
    /// <summary>Time zones for the clinic form: main US zones first, then all others by current offset.</summary>
    [HttpGet("time-zones")]
    public IActionResult GetTimeZones() =>
        HandleResult(Result<IReadOnlyList<TimeZoneOptionDto>>.Ok(TimeZoneCatalog.List(dateTimeProvider.UtcNow)));

    /// <summary>The slot intervals (minutes) a clinic may choose.</summary>
    [HttpGet("slot-intervals")]
    public IActionResult GetSlotIntervals() =>
        HandleResult(Result<IReadOnlyList<int>>.Ok(ClinicRules.AllowedSlotIntervals));
}
