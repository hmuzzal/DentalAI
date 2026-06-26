using DentalAI.Application.Common.Interfaces.Persistence;
using DentalAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DentalAI.Infrastructure.Persistence.Repositories;

public sealed class PatientRepository(ApplicationDbContext dbContext) : IPatientRepository
{
    public Task AddAsync(Patient patient, CancellationToken cancellationToken) => dbContext.Patients.AddAsync(patient, cancellationToken).AsTask();

    public Task<Patient?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Patients.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Patient>> ListAsync(Guid? clinicId, CancellationToken cancellationToken)
    {
        var query = dbContext.Patients.AsNoTracking().AsQueryable();

        if (clinicId.HasValue)
        {
            query = query.Where(x => x.ClinicId == clinicId.Value);
        }

        return await query
            .OrderBy(x => x.LastName)
            .ThenBy(x => x.FirstName)
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountAsync(CancellationToken cancellationToken) => dbContext.Patients.CountAsync(cancellationToken);

    public void Update(Patient patient) => dbContext.Patients.Update(patient);
    public void Remove(Patient patient) => dbContext.Patients.Remove(patient);
}
