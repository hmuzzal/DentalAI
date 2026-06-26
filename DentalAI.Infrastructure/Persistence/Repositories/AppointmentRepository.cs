using DentalAI.Application.Common.Interfaces.Persistence;
using DentalAI.Domain.Entities;
using DentalAI.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DentalAI.Infrastructure.Persistence.Repositories;

public sealed class AppointmentRepository(ApplicationDbContext dbContext) : IAppointmentRepository
{
    public Task AddAsync(Appointment appointment, CancellationToken cancellationToken) => dbContext.Appointments.AddAsync(appointment, cancellationToken).AsTask();

    public Task<Appointment?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Appointments
            .Include(x => x.Doctor)
            .Include(x => x.Patient)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Appointment>> ListAsync(Guid? clinicId, CancellationToken cancellationToken)
    {
        var query = dbContext.Appointments
            .AsNoTracking()
            .Include(x => x.Doctor)
            .Include(x => x.Patient)
            .AsQueryable();

        if (clinicId.HasValue)
        {
            query = query.Where(x => x.ClinicId == clinicId.Value);
        }

        return await query
            .OrderBy(x => x.StartTime)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> HasOverlapAsync(Guid doctorId, DateTime startUtc, DateTime endUtc, Guid? excludedAppointmentId, CancellationToken cancellationToken) =>
        dbContext.Appointments.AnyAsync(x =>
            x.DoctorId == doctorId
            && x.Status != AppointmentStatus.Cancelled
            && x.Status != AppointmentStatus.NoShow
            && (!excludedAppointmentId.HasValue || x.Id != excludedAppointmentId.Value)
            && startUtc < x.EndTime
            && endUtc > x.StartTime, cancellationToken);

    public Task<int> CountTodayAsync(DateTime dayStartUtc, DateTime dayEndUtc, CancellationToken cancellationToken) =>
        dbContext.Appointments.CountAsync(x => x.StartTime >= dayStartUtc && x.StartTime < dayEndUtc, cancellationToken);

    public Task<int> CountUpcomingAsync(DateTime utcNow, CancellationToken cancellationToken) =>
        dbContext.Appointments.CountAsync(x =>
            x.StartTime > utcNow
            && (x.Status == AppointmentStatus.Pending || x.Status == AppointmentStatus.Confirmed), cancellationToken);

    public void Update(Appointment appointment) => dbContext.Appointments.Update(appointment);
}
