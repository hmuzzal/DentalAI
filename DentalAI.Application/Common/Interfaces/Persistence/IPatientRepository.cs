using DentalAI.Domain.Entities;

namespace DentalAI.Application.Common.Interfaces.Persistence;

public interface IPatientRepository
{
    Task AddAsync(Patient patient, CancellationToken cancellationToken);
    Task<Patient?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Patient>> ListAsync(Guid? clinicId, CancellationToken cancellationToken);
    Task<int> CountAsync(CancellationToken cancellationToken);
    void Update(Patient patient);
    void Remove(Patient patient);
}
