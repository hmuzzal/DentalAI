using DentalAI.Domain.Entities;

namespace DentalAI.Application.Common.Interfaces.Persistence;

public interface IServiceRepository
{
    Task AddAsync(Service service, CancellationToken cancellationToken);
    Task<Service?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Service>> ListAsync(CancellationToken cancellationToken);
    void Update(Service service);
    void Remove(Service service);
}
