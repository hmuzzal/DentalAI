using DentalAI.Domain.Entities;

namespace DentalAI.Application.Common.Interfaces.Persistence;

public interface IDoctorRepository
{
    Task AddAsync(Doctor doctor, CancellationToken cancellationToken);
    Task<Doctor?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Doctor>> ListAsync(Guid? clinicId, CancellationToken cancellationToken);
    void Update(Doctor doctor);
    void Remove(Doctor doctor);
}
