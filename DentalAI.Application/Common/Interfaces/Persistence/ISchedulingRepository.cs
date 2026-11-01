using DentalAI.Application.Common.Models;
using DentalAI.Application.DTOs.Calendar;
using DentalAI.Application.DTOs.Dashboard;
using DentalAI.Domain.Entities;

namespace DentalAI.Application.Common.Interfaces.Persistence;

public interface ISchedulingRepository
{
    Task<SchedulingSnapshot?> GetSnapshotAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken);
    Task<IReadOnlyList<DoctorSchedule>> GetSchedulesAsync(Guid doctorId, CancellationToken cancellationToken);
    Task<IReadOnlyList<DoctorBreak>> GetBreaksAsync(Guid doctorId, CancellationToken cancellationToken);
    Task<IReadOnlyList<DoctorLeave>> GetLeavesAsync(Guid doctorId, CancellationToken cancellationToken);
    Task ReplaceAvailabilityAsync(Guid doctorId, Guid? clinicId, IReadOnlyList<DoctorSchedule> schedules, IReadOnlyList<DoctorBreak> breaks, IReadOnlyList<DoctorLeave> leaves, CancellationToken cancellationToken);
    Task<IReadOnlyList<CalendarAppointmentDto>> GetCalendarAsync(Guid? clinicId, Guid? doctorId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);
    Task<SchedulingDashboardDto> GetDashboardAsync(DateTime todayStartUtc, DateTime todayEndUtc, DateTime utcNow, CancellationToken cancellationToken);
}
