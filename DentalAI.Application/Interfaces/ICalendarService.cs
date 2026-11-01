using DentalAI.Application.Common.Models;
using DentalAI.Application.DTOs.Calendar;

namespace DentalAI.Application.Interfaces;

public interface ICalendarService
{
    Task<Result<CalendarDto>> GetDayAsync(CalendarQuery query, CancellationToken cancellationToken);
    Task<Result<CalendarDto>> GetWeekAsync(CalendarQuery query, CancellationToken cancellationToken);
    Task<Result<CalendarDto>> GetMonthAsync(CalendarQuery query, CancellationToken cancellationToken);
}
