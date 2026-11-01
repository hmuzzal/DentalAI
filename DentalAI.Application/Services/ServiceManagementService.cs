using AutoMapper;
using DentalAI.Application.Common.Interfaces;
using DentalAI.Application.Common.Interfaces.Persistence;
using DentalAI.Application.Common.Models;
using DentalAI.Application.DTOs.Services;
using DentalAI.Application.Interfaces;

namespace DentalAI.Application.Services;

public sealed class ServiceManagementService(
    IServiceRepository serviceRepository,
    IClinicRepository clinicRepository,
    ICurrentUserService currentUserService,
    IUnitOfWork unitOfWork,
    IMapper mapper) : IServiceManagementService
{
    public async Task<Result<IReadOnlyList<ServiceDto>>> ListAsync(CancellationToken cancellationToken)
    {
        var services = await serviceRepository.ListAsync(cancellationToken);
        return Result<IReadOnlyList<ServiceDto>>.Ok(mapper.Map<IReadOnlyList<ServiceDto>>(services));
    }

    public async Task<Result<ServiceDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var service = await serviceRepository.GetByIdAsync(id, cancellationToken);
        return service is null
            ? Result<ServiceDto>.Fail(ResultStatus.NotFound, "Service was not found.")
            : Result<ServiceDto>.Ok(mapper.Map<ServiceDto>(service));
    }

    public async Task<Result<ServiceDto>> CreateAsync(UpsertServiceRequest request, CancellationToken cancellationToken)
    {
        var clinicId = currentUserService.ClinicId;
        if (!clinicId.HasValue)
        {
            return Result<ServiceDto>.Fail(ResultStatus.Forbidden, "Clinic context is required.");
        }

        if (await clinicRepository.GetByIdAsync(clinicId.Value, cancellationToken) is null)
        {
            return Result<ServiceDto>.Fail(ResultStatus.NotFound, "Clinic was not found.");
        }

        var service = new DentalAI.Domain.Entities.Service
        {
            ClinicId = clinicId.Value,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            Price = request.Price,
            DurationMinutes = request.DurationMinutes,
            IsActive = request.IsActive
        };

        await serviceRepository.AddAsync(service, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<ServiceDto>.Ok(mapper.Map<ServiceDto>(service), "Service created successfully.");
    }

    public async Task<Result<ServiceDto>> UpdateAsync(Guid id, UpsertServiceRequest request, CancellationToken cancellationToken)
    {
        var service = await serviceRepository.GetByIdAsync(id, cancellationToken);
        if (service is null)
        {
            return Result<ServiceDto>.Fail(ResultStatus.NotFound, "Service was not found.");
        }

        service.Name = request.Name.Trim();
        service.Description = request.Description?.Trim();
        service.Price = request.Price;
        service.DurationMinutes = request.DurationMinutes;
        service.IsActive = request.IsActive;

        serviceRepository.Update(service);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<ServiceDto>.Ok(mapper.Map<ServiceDto>(service), "Service updated successfully.");
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var service = await serviceRepository.GetByIdAsync(id, cancellationToken);
        if (service is null)
        {
            return Result.Fail(ResultStatus.NotFound, "Service was not found.");
        }

        serviceRepository.Remove(service);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Ok("Service deleted successfully.");
    }
}
