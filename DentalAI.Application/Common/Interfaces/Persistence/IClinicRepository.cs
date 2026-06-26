using DentalAI.Domain.Entities;

namespace DentalAI.Application.Common.Interfaces.Persistence;

public interface IClinicRepository
{
    Task AddAsync(Clinic clinic, CancellationToken cancellationToken);
    Task<Clinic?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<Clinic?> GetByNameAsync(string name, CancellationToken cancellationToken);
    Task<IReadOnlyList<Clinic>> ListAsync(CancellationToken cancellationToken);
    void Update(Clinic clinic);
}
