using DentalAI.Domain.Entities;

namespace DentalAI.Application.Common.Interfaces.Persistence;

public interface IPatientRepository
{
    Task AddAsync(Patient patient, CancellationToken cancellationToken);
    /// <summary>Tracked, with its (non-deleted) insurance policies.</summary>
    Task<Patient?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Patient>> ListAsync(Guid? clinicId, CancellationToken cancellationToken);
    Task<int> CountAsync(CancellationToken cancellationToken);

    /// <summary>A page of patients whose name, phone or email contains <paramref name="search"/>, plus the total match count.</summary>
    Task<(IReadOnlyList<Patient> Items, int TotalCount)> SearchAsync(
        Guid? clinicId, string? search, int page, int pageSize, CancellationToken cancellationToken);
    void Update(Patient patient);
    void Remove(Patient patient);
    void AddInsurance(PatientInsurance insurance);
    void RemoveInsurance(PatientInsurance insurance);
}
