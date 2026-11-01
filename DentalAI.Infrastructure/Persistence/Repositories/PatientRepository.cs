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

    public async Task<(IReadOnlyList<Patient> Items, int TotalCount)> SearchAsync(
        Guid? clinicId, string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = dbContext.Patients.AsNoTracking().AsQueryable();

        if (clinicId.HasValue)
        {
            query = query.Where(x => x.ClinicId == clinicId.Value);
        }

        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            // Each word must match somewhere, so "jane smith" finds Jane Smith. Contains() becomes a
            // parameterised LIKE; the column collation decides case sensitivity (insensitive by default).
            foreach (var word in term.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(5))
            {
                query = query.Where(x => x.FirstName.Contains(word)
                    || x.LastName.Contains(word)
                    || x.Phone.Contains(word)
                    || (x.Email != null && x.Email.Contains(word)));
            }
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.LastName)
            .ThenBy(x => x.FirstName)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public void Update(Patient patient) => dbContext.Patients.Update(patient);
    public void Remove(Patient patient) => dbContext.Patients.Remove(patient);
}
