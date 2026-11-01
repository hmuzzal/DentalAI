using DentalAI.Application.Common.Interfaces.Persistence;
using DentalAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DentalAI.Infrastructure.Persistence.Repositories;

public sealed class UserRepository(ApplicationDbContext dbContext) : IUserRepository
{
    public Task AddAsync(User user, CancellationToken cancellationToken) => dbContext.Users.AddAsync(user, cancellationToken).AsTask();

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken) =>
        dbContext.Users
            .IgnoreQueryFilters()
            .Include(x => x.Clinic)
            .FirstOrDefaultAsync(x => x.Email == email && !x.IsDeleted, cancellationToken);

    // By the signed-in user's own ID (from the token). Unfiltered so platform admins, who have no clinic, can load
    // their own profile.
    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Users
            .IgnoreQueryFilters()
            .Include(x => x.Clinic)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);

    public Task<User?> GetByRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken) =>
        dbContext.Users
            .IgnoreQueryFilters()
            .Include(x => x.Clinic)
            .FirstOrDefaultAsync(x => x.RefreshToken == refreshToken && !x.IsDeleted, cancellationToken);

    // Staff queries take the clinic explicitly (platform admins have no clinic of their own), so they skip the
    // tenant filter and filter by clinic themselves.
    public Task<User?> GetByInviteTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        dbContext.Users.IgnoreQueryFilters().Include(x => x.Clinic)
            .FirstOrDefaultAsync(x => x.InviteTokenHash == tokenHash && !x.IsDeleted, cancellationToken);

    public async Task<IReadOnlyList<User>> ListByClinicAsync(Guid clinicId, CancellationToken cancellationToken) =>
        await dbContext.Users.IgnoreQueryFilters()
            .Where(x => x.ClinicId == clinicId && !x.IsDeleted)
            .OrderBy(x => x.LastName).ThenBy(x => x.FirstName)
            .ToListAsync(cancellationToken);

    public Task<User?> GetInClinicAsync(Guid userId, Guid clinicId, CancellationToken cancellationToken) =>
        dbContext.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == userId && x.ClinicId == clinicId && !x.IsDeleted, cancellationToken);

    // Mark only the user as modified. Users.Update() walks the graph and would also
    // mark the included Clinic as modified (and bump its audit fields).
    public void Update(User user) => dbContext.Entry(user).State = EntityState.Modified;
}
