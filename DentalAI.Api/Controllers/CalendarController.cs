using DentalAI.Application.DTOs.Calendar;
using DentalAI.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalAI.Api.Controllers;

[Authorize(Policy = Infrastructure.AuthorizationPolicies.ReceptionistAccess)]
public sealed class CalendarController(ICalendarService calendarService) : ApiControllerBase
{
    [HttpGet("day")]
    public async Task<IActionResult> Day([FromQuery] CalendarQuery query, CancellationToken cancellationToken) =>
        HandleResult(await calendarService.GetDayAsync(query, cancellationToken));

    [HttpGet("week")]
    public async Task<IActionResult> Week([FromQuery] CalendarQuery query, CancellationToken cancellationToken) =>
        HandleResult(await calendarService.GetWeekAsync(query, cancellationToken));

    [HttpGet("month")]
    public async Task<IActionResult> Month([FromQuery] CalendarQuery query, CancellationToken cancellationToken) =>
        HandleResult(await calendarService.GetMonthAsync(query, cancellationToken));
}
