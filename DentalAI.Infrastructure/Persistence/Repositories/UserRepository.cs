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

    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Users
            .Include(x => x.Clinic)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<User?> GetByRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken) =>
        dbContext.Users
            .IgnoreQueryFilters()
            .Include(x => x.Clinic)
            .FirstOrDefaultAsync(x => x.RefreshToken == refreshToken && !x.IsDeleted, cancellationToken);

    // Mark only the user as modified. Users.Update() walks the graph and would also
    // mark the included Clinic as modified (and bump its audit fields).
    public void Update(User user) => dbContext.Entry(user).State = EntityState.Modified;
}
