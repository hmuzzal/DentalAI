namespace DentalAI.Application.Common.Interfaces.Persistence;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runs <paramref name="work"/> in a transaction while holding an exclusive lock on one doctor's
    /// schedule, so "check the slot is free, then save" cannot interleave with another booking for the
    /// same doctor. Do the availability check and the save inside <paramref name="work"/>.
    /// </summary>
    Task<T> RunWithDoctorScheduleLockAsync<T>(Guid doctorId, Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken);

    /// <summary>
    /// Runs <paramref name="work"/> in a transaction holding an exclusive lock named <paramref name="resource"/>
    /// (e.g. "invoice:{id}"). Load what you change inside <paramref name="work"/>, so you see committed data.
    /// Can be nested: the inner call joins the outer transaction.
    /// </summary>
    Task<T> RunWithLockAsync<T>(string resource, Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken);
}
