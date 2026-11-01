using DentalAI.Application.Common.Interfaces;
using DentalAI.Application.Common.Interfaces.Persistence;
using DentalAI.Application.Common.Models;
using DentalAI.Application.DTOs.Calendar;
using DentalAI.Application.Interfaces;

namespace DentalAI.Application.Services;

public sealed class CalendarService(
    ISchedulingRepository repository,
    IClinicRepository clinicRepository,
    ICurrentUserService currentUserService) : ICalendarService
{
    public Task<Result<CalendarDto>> GetDayAsync(CalendarQuery query, CancellationToken cancellationToken) =>
        GetRangeAsync(query, query.Date, query.Date, cancellationToken);

    public Task<Result<CalendarDto>> GetWeekAsync(CalendarQuery query, CancellationToken cancellationToken)
    {
        // Weeks run Sunday to Saturday (US convention).
        var offset = (int)query.Date.DayOfWeek;
        var start = query.Date.AddDays(-offset);
        return GetRangeAsync(query, start, start.AddDays(6), cancellationToken);
    }

    public Task<Result<CalendarDto>> GetMonthAsync(CalendarQuery query, CancellationToken cancellationToken)
    {
        var start = new DateOnly(query.Date.Year, query.Date.Month, 1);
        return GetRangeAsync(query, start, start.AddMonths(1).AddDays(-1), cancellationToken);
    }

    private async Task<Result<CalendarDto>> GetRangeAsync(CalendarQuery query, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var clinicId = currentUserService.ClinicId;
        if (!clinicId.HasValue)
            return Result<CalendarDto>.Fail(ResultStatus.Forbidden, "Clinic context is required.");

        var clinic = await clinicRepository.GetByIdAsync(clinicId.Value, cancellationToken);
        var timeZone = SchedulingEngine.ResolveTimeZone(clinic?.TimeZone);
        var fromUtc = SchedulingEngine.ToUtc(from, TimeOnly.MinValue, timeZone);
        var toUtc = SchedulingEngine.ToUtc(to.AddDays(1), TimeOnly.MinValue, timeZone);
        var items = await repository.GetCalendarAsync(clinicId, query.DoctorId, fromUtc, toUtc, cancellationToken);
        return Result<CalendarDto>.Ok(new CalendarDto(from, to, items));
    }
}
