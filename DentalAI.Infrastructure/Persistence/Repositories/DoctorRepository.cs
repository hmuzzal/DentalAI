using DentalAI.Application.Common.Interfaces.Persistence;
using DentalAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DentalAI.Infrastructure.Persistence.Repositories;

public sealed class DoctorRepository(ApplicationDbContext dbContext) : IDoctorRepository
{
    public Task AddAsync(Doctor doctor, CancellationToken cancellationToken) => dbContext.Doctors.AddAsync(doctor, cancellationToken).AsTask();

    public Task<Doctor?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Doctors.Include(x => x.Clinic).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Doctor>> ListAsync(Guid? clinicId, CancellationToken cancellationToken)
    {
        var query = dbContext.Doctors.AsNoTracking().Include(x => x.Clinic).AsQueryable();

        if (clinicId.HasValue)
        {
            query = query.Where(x => x.ClinicId == clinicId.Value);
        }

        return await query
            .OrderBy(x => x.FirstName)
            .ThenBy(x => x.LastName)
            .ToListAsync(cancellationToken);
    }

    public void Update(Doctor doctor) => dbContext.Doctors.Update(doctor);
    public void Remove(Doctor doctor) => dbContext.Doctors.Remove(doctor);
}
