using DentalAI.Application.Common.Models;
using DentalAI.Application.DTOs.Calendar;
using DentalAI.Application.DTOs.Dashboard;
using DentalAI.Domain.Entities;

namespace DentalAI.Application.Common.Interfaces.Persistence;

public interface ISchedulingRepository
{
    /// <param name="excludeAppointmentId">An appointment to ignore, e.g. the one being rescheduled.</param>
    Task<SchedulingSnapshot?> GetSnapshotAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken, Guid? excludeAppointmentId = null);
    Task<IReadOnlyList<DoctorSchedule>> GetSchedulesAsync(Guid doctorId, CancellationToken cancellationToken);
    Task<IReadOnlyList<DoctorBreak>> GetBreaksAsync(Guid doctorId, CancellationToken cancellationToken);
    Task<IReadOnlyList<DoctorLeave>> GetLeavesAsync(Guid doctorId, CancellationToken cancellationToken);
    Task ReplaceAvailabilityAsync(Guid doctorId, Guid? clinicId, IReadOnlyList<DoctorSchedule> schedules, IReadOnlyList<DoctorBreak> breaks, IReadOnlyList<DoctorLeave> leaves, CancellationToken cancellationToken);
    Task<IReadOnlyList<CalendarAppointmentDto>> GetCalendarAsync(Guid? clinicId, Guid? doctorId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);
    /// <summary>Appointments starting in [fromUtc, toUtc) with price and clinic zone, for DashboardCalculator.</summary>
    Task<IReadOnlyList<DashboardAppointmentRow>> GetDashboardRowsAsync(DateTime fromUtc, DateTime toUtc, bool completedOnly, CancellationToken cancellationToken);
}
