using DentalAI.Application.Common.Models;
using DentalAI.Application.DTOs.Services;

namespace DentalAI.Application.Interfaces;

public interface IServiceManagementService
{
    Task<Result<IReadOnlyList<ServiceDto>>> ListAsync(CancellationToken cancellationToken);
    Task<Result<ServiceDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<ServiceDto>> CreateAsync(UpsertServiceRequest request, CancellationToken cancellationToken);
    Task<Result<ServiceDto>> UpdateAsync(Guid id, UpsertServiceRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
