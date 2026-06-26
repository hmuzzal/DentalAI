using DentalAI.Application.Common.Models;
using DentalAI.Application.DTOs.Clinics;

namespace DentalAI.Application.Interfaces;

public interface IClinicService
{
    Task<Result<IReadOnlyList<ClinicDto>>> ListAsync(CancellationToken cancellationToken);
    Task<Result<ClinicDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<ClinicDto>> CreateAsync(CreateClinicDto request, CancellationToken cancellationToken);
    Task<Result<ClinicDto>> UpdateAsync(Guid id, UpdateClinicDto request, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
