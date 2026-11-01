using DentalAI.Application.Common.Interfaces.Persistence;
using DentalAI.Application.Common.Models;
using DentalAI.Application.DTOs.Calendar;
using DentalAI.Application.DTOs.Dashboard;
using DentalAI.Application.Services;
using DentalAI.Domain.Entities;
using DentalAI.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DentalAI.Infrastructure.Persistence.Repositories;

public sealed class SchedulingRepository(ApplicationDbContext dbContext) : ISchedulingRepository
{
    public async Task<SchedulingSnapshot?> GetSnapshotAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken)
    {
        var doctor = await dbContext.Doctors.AsNoTracking()
            .Where(x => x.Id == doctorId)
            .Select(x => new { x.Id, x.ClinicId, TimeZone = x.Clinic != null ? x.Clinic.TimeZone : "UTC" })
            .SingleOrDefaultAsync(cancellationToken);
        if (doctor is null) return null;

        var day = date.DayOfWeek;
        var timeZone = SchedulingEngine.ResolveTimeZone(doctor.TimeZone);
        var fromUtc = SchedulingEngine.ToUtc(date, TimeOnly.MinValue, timeZone);
        var toUtc = SchedulingEngine.ToUtc(date.AddDays(1), TimeOnly.MinValue, timeZone);

        var schedules = await dbContext.DoctorSchedules.AsNoTracking()
            .Where(x => x.DoctorId == doctorId && x.DayOfWeek == day && x.IsWorking)
            .Select(x => new ScheduleWindow(x.StartTime, x.EndTime))
            .ToListAsync(cancellationToken);
        var breaks = await dbContext.DoctorBreaks.AsNoTracking()
            .Where(x => x.DoctorId == doctorId && x.DayOfWeek == day)
            .Select(x => new ScheduleWindow(x.StartTime, x.EndTime))
            .ToListAsync(cancellationToken);
        var onLeave = await dbContext.DoctorLeaves.AsNoTracking()
            .AnyAsync(x => x.DoctorId == doctorId && x.StartDate <= date && x.EndDate >= date, cancellationToken);
        var appointments = await dbContext.Appointments.AsNoTracking()
            .Where(x => x.DoctorId == doctorId
                && x.Status != AppointmentStatus.Cancelled
                && x.Status != AppointmentStatus.NoShow
                && x.StartTime < toUtc && x.EndTime > fromUtc)
            .Select(x => new AppointmentWindow(x.StartTime, x.EndTime))
            .ToListAsync(cancellationToken);

        return new SchedulingSnapshot(doctor.Id, doctor.ClinicId, doctor.TimeZone, date, schedules, breaks, onLeave, appointments);
    }

    public async Task<IReadOnlyList<DoctorSchedule>> GetSchedulesAsync(Guid doctorId, CancellationToken cancellationToken) =>
        await dbContext.DoctorSchedules.AsNoTracking().Where(x => x.DoctorId == doctorId)
            .OrderBy(x => x.DayOfWeek).ThenBy(x => x.StartTime).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DoctorBreak>> GetBreaksAsync(Guid doctorId, CancellationToken cancellationToken) =>
        await dbContext.DoctorBreaks.AsNoTracking().Where(x => x.DoctorId == doctorId)
            .OrderBy(x => x.DayOfWeek).ThenBy(x => x.StartTime).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DoctorLeave>> GetLeavesAsync(Guid doctorId, CancellationToken cancellationToken) =>
        await dbContext.DoctorLeaves.AsNoTracking().Where(x => x.DoctorId == doctorId)
            .OrderBy(x => x.StartDate).ToListAsync(cancellationToken);

    public async Task ReplaceAvailabilityAsync(Guid doctorId, Guid? clinicId, IReadOnlyList<DoctorSchedule> schedules, IReadOnlyList<DoctorBreak> breaks, IReadOnlyList<DoctorLeave> leaves, CancellationToken cancellationToken)
    {
        dbContext.DoctorSchedules.RemoveRange(await dbContext.DoctorSchedules.Where(x => x.DoctorId == doctorId).ToListAsync(cancellationToken));
        dbContext.DoctorBreaks.RemoveRange(await dbContext.DoctorBreaks.Where(x => x.DoctorId == doctorId).ToListAsync(cancellationToken));
        dbContext.DoctorLeaves.RemoveRange(await dbContext.DoctorLeaves.Where(x => x.DoctorId == doctorId).ToListAsync(cancellationToken));
        await dbContext.DoctorSchedules.AddRangeAsync(schedules, cancellationToken);
        await dbContext.DoctorBreaks.AddRangeAsync(breaks, cancellationToken);
        await dbContext.DoctorLeaves.AddRangeAsync(leaves, cancellationToken);
    }

    public async Task<IReadOnlyList<CalendarAppointmentDto>> GetCalendarAsync(Guid? clinicId, Guid? doctorId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
    {
        var query = dbContext.Appointments.AsNoTracking()
            .Where(x => x.StartTime < toUtc && x.EndTime > fromUtc);
        if (clinicId.HasValue) query = query.Where(x => x.ClinicId == clinicId);
        if (doctorId.HasValue) query = query.Where(x => x.DoctorId == doctorId);

        return await query.OrderBy(x => x.StartTime)
            .Select(x => new CalendarAppointmentDto(
                x.Id, x.ClinicId, x.DoctorId,
                x.Doctor!.FirstName + " " + x.Doctor.LastName,
                x.PatientId, x.Patient!.FirstName + " " + x.Patient.LastName,
                x.ServiceId, x.Service!.Name, x.StartTime, x.EndTime, x.Status))
            .ToListAsync(cancellationToken);
    }

    public async Task<SchedulingDashboardDto> GetDashboardAsync(DateTime todayStartUtc, DateTime todayEndUtc, DateTime utcNow, CancellationToken cancellationToken)
    {
        var today = dbContext.Appointments.AsNoTracking().Where(x => x.StartTime >= todayStartUtc && x.StartTime < todayEndUtc);
        var revenueStart = new DateTime(todayStartUtc.Year, todayStartUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-11);

        var counts = await today.GroupBy(_ => 1).Select(g => new
        {
            Total = g.Count(),
            Completed = g.Count(x => x.Status == AppointmentStatus.Completed),
            Cancelled = g.Count(x => x.Status == AppointmentStatus.Cancelled),
            NoShows = g.Count(x => x.Status == AppointmentStatus.NoShow)
        }).SingleOrDefaultAsync(cancellationToken);
        var upcoming = await dbContext.Appointments.AsNoTracking().CountAsync(x => x.StartTime > utcNow
            && (x.Status == AppointmentStatus.Pending || x.Status == AppointmentStatus.Confirmed), cancellationToken);
        var completed = dbContext.Appointments.AsNoTracking()
            .Where(x => x.Status == AppointmentStatus.Completed && x.StartTime >= revenueStart);
        var byDayRaw = await completed.Where(x => x.StartTime >= todayStartUtc.AddDays(-29))
            .GroupBy(x => x.StartTime.Date)
            .Select(g => new { Date = g.Key, Revenue = g.Sum(x => x.Service!.Price) })
            .OrderBy(x => x.Date).ToListAsync(cancellationToken);
        var byDay = byDayRaw.Select(x => new RevenuePointDto(x.Date.ToString("yyyy-MM-dd"), x.Revenue)).ToList();
        var byMonthRaw = await completed
            .GroupBy(x => new { x.StartTime.Year, x.StartTime.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Revenue = g.Sum(x => x.Service!.Price) })
            .OrderBy(x => x.Year).ThenBy(x => x.Month).ToListAsync(cancellationToken);
        var byMonth = byMonthRaw.Select(x => new RevenuePointDto($"{x.Year:D4}-{x.Month:D2}", x.Revenue)).ToList();

        return new SchedulingDashboardDto(counts?.Total ?? 0, upcoming, counts?.Completed ?? 0, counts?.Cancelled ?? 0,
            counts?.NoShows ?? 0, byDay, byMonth);
    }
}
