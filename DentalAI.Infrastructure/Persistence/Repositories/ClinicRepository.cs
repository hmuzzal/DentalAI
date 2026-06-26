using DentalAI.Application.Common.Interfaces.Persistence;
using DentalAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DentalAI.Infrastructure.Persistence.Repositories;

public sealed class ClinicRepository(ApplicationDbContext dbContext) : IClinicRepository
{
    public Task AddAsync(Clinic clinic, CancellationToken cancellationToken) => dbContext.Clinics.AddAsync(clinic, cancellationToken).AsTask();

    public Task<Clinic?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Clinics.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<Clinic?> GetByNameAsync(string name, CancellationToken cancellationToken) =>
        dbContext.Clinics.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Name == name && !x.IsDeleted, cancellationToken);

    public async Task<IReadOnlyList<Clinic>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.Clinics.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken);

    public void Update(Clinic clinic) => dbContext.Clinics.Update(clinic);
}
