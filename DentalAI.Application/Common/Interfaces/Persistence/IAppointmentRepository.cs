using DentalAI.Domain.Entities;

namespace DentalAI.Application.Common.Interfaces.Persistence;

public interface IAppointmentRepository
{
    Task AddAsync(Appointment appointment, CancellationToken cancellationToken);
    Task<Appointment?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Appointment>> ListAsync(Guid? clinicId, CancellationToken cancellationToken);
    Task<bool> HasOverlapAsync(Guid doctorId, DateTime startUtc, DateTime endUtc, Guid? excludedAppointmentId, CancellationToken cancellationToken);
    Task<int> CountUpcomingAsync(DateTime utcNow, CancellationToken cancellationToken);
    /// <summary>Pending or confirmed appointments for the doctor that start after <paramref name="utcNow"/>.</summary>
    Task<int> CountUpcomingForDoctorAsync(Guid doctorId, DateTime utcNow, CancellationToken cancellationToken);
    void Update(Appointment appointment);
}
