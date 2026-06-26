using DentalAI.Domain.Entities;

namespace DentalAI.Application.Common.Interfaces.Persistence;

public interface IAppointmentRepository
{
    Task AddAsync(Appointment appointment, CancellationToken cancellationToken);
    Task<Appointment?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Appointment>> ListAsync(Guid? clinicId, CancellationToken cancellationToken);
    Task<bool> HasOverlapAsync(Guid doctorId, DateTime startUtc, DateTime endUtc, Guid? excludedAppointmentId, CancellationToken cancellationToken);
    Task<int> CountTodayAsync(DateTime dayStartUtc, DateTime dayEndUtc, CancellationToken cancellationToken);
    Task<int> CountUpcomingAsync(DateTime utcNow, CancellationToken cancellationToken);
    void Update(Appointment appointment);
}
