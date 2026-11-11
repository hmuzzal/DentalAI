namespace DentalAI.Application.Common.Interfaces;

/// <summary>
/// Each staff request checks the account is still active, its clinic still exists and its role is the one in the
/// token (cached briefly). Call <see cref="Forget"/> after deactivating someone, changing their role or deleting
/// their clinic, so their current access token stops working at once instead of when the cache entry expires.
/// </summary>
public interface IStaffAccessCache
{
    void Forget(Guid userId);
}
