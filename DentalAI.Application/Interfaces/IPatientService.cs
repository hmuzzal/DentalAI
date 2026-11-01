using DentalAI.Application.Common.Models;
using DentalAI.Application.DTOs.Patients;

namespace DentalAI.Application.Interfaces;

public interface IPatientService
{
    Task<Result<IReadOnlyList<PatientDto>>> ListAsync(Guid? clinicId, CancellationToken cancellationToken);
    Task<Result<PagedResult<PatientDto>>> SearchAsync(PatientSearchQuery query, CancellationToken cancellationToken);
    Task<Result<PatientDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<PatientDto>> CreateAsync(UpsertPatientRequest request, CancellationToken cancellationToken);
    Task<Result<PatientDto>> UpdateAsync(Guid id, UpsertPatientRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
