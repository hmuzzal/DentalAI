using DentalAI.Application.Common.Models;
using DentalAI.Application.DTOs.Doctors;

namespace DentalAI.Application.Interfaces;

public interface IDoctorService
{
    Task<Result<IReadOnlyList<DoctorDto>>> ListAsync(CancellationToken cancellationToken);
    Task<Result<DoctorDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<DoctorDto>> CreateAsync(CreateDoctorDto request, CancellationToken cancellationToken);
    Task<Result<DoctorDto>> UpdateAsync(Guid id, UpdateDoctorDto request, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
