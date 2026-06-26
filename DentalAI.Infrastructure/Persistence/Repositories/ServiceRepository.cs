using DentalAI.Application.Common.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DentalAI.Infrastructure.Persistence.Repositories;

public sealed class ServiceRepository(ApplicationDbContext dbContext) : IServiceRepository
{
    public Task AddAsync(Domain.Entities.Service service, CancellationToken cancellationToken) => dbContext.Services.AddAsync(service, cancellationToken).AsTask();

    public Task<Domain.Entities.Service?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Services.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Domain.Entities.Service>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.Services.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken);

    public void Update(Domain.Entities.Service service) => dbContext.Services.Update(service);
    public void Remove(Domain.Entities.Service service) => dbContext.Services.Remove(service);
}
