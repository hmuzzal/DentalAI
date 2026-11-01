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
    public async Task<SchedulingSnapshot?> GetSnapshotAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken, Guid? excludeAppointmentId = null)
    {
        var doctor = await dbContext.Doctors.AsNoTracking()
            .Where(x => x.Id == doctorId)
            .Select(x => new
            {
                x.Id,
                x.ClinicId,
                TimeZone = x.Clinic != null ? x.Clinic.TimeZone : "UTC",
                SlotIntervalMinutes = x.Clinic != null ? x.Clinic.SlotIntervalMinutes : SchedulingEngine.DefaultSlotIntervalMinutes
            })
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
                && (!excludeAppointmentId.HasValue || x.Id != excludeAppointmentId.Value)
                && x.Status != AppointmentStatus.Cancelled
                && x.Status != AppointmentStatus.NoShow
                && x.StartTime < toUtc && x.EndTime > fromUtc)
            .Select(x => new AppointmentWindow(x.StartTime, x.EndTime))
            .ToListAsync(cancellationToken);

        return new SchedulingSnapshot(doctor.Id, doctor.ClinicId, doctor.TimeZone, date, schedules, breaks, onLeave,
            appointments, doctor.SlotIntervalMinutes);
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
                x.ServiceId, x.Service!.Name, x.StartTime, x.EndTime, x.Status,
                x.Clinic != null ? x.Clinic.TimeZone : null))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DashboardAppointmentRow>> GetDashboardRowsAsync(DateTime fromUtc, DateTime toUtc, bool completedOnly, CancellationToken cancellationToken)
    {
        var query = dbContext.Appointments.AsNoTracking().Where(x => x.StartTime >= fromUtc && x.StartTime < toUtc);
        if (completedOnly) query = query.Where(x => x.Status == AppointmentStatus.Completed);

        return await query
            .Select(x => new DashboardAppointmentRow(
                x.StartTime,
                x.Status,
                x.Service != null ? x.Service.Price : 0m,
                x.Clinic != null ? x.Clinic.TimeZone : null))
            .ToListAsync(cancellationToken);
    }
}
