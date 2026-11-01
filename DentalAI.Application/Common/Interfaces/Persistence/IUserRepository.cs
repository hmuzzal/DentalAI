using DentalAI.Domain.Entities;

namespace DentalAI.Application.Common.Interfaces.Persistence;

public interface IUserRepository
{
    Task AddAsync(User user, CancellationToken cancellationToken);
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken);
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<User?> GetByRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken);
    /// <summary>The user whose invite token hashes to <paramref name="tokenHash"/>, with their clinic.</summary>
    Task<User?> GetByInviteTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>A clinic's staff (not deleted), whoever is asking. Callers must pass a clinic they may manage.</summary>
    Task<IReadOnlyList<User>> ListByClinicAsync(Guid clinicId, CancellationToken cancellationToken);

    /// <summary>A staff member of the given clinic, tracked; null if they belong to another clinic.</summary>
    Task<User?> GetInClinicAsync(Guid userId, Guid clinicId, CancellationToken cancellationToken);

    void Update(User user);
}
