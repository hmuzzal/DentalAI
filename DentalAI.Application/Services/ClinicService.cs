using AutoMapper;
using DentalAI.Application.Common.Interfaces.Persistence;
using DentalAI.Application.Common.Models;
using DentalAI.Application.DTOs.Clinics;
using DentalAI.Application.Interfaces;
using DentalAI.Domain.Entities;

namespace DentalAI.Application.Services;

public sealed class ClinicService(
    IClinicRepository clinicRepository,
    IUnitOfWork unitOfWork,
    IMapper mapper) : IClinicService
{
    public async Task<Result<IReadOnlyList<ClinicDto>>> ListAsync(CancellationToken cancellationToken)
    {
        var clinics = await clinicRepository.ListAsync(cancellationToken);
        return Result<IReadOnlyList<ClinicDto>>.Ok(mapper.Map<IReadOnlyList<ClinicDto>>(clinics));
    }

    public async Task<Result<ClinicDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var clinic = await clinicRepository.GetByIdAsync(id, cancellationToken);
        return clinic is null
            ? Result<ClinicDto>.Fail(ResultStatus.NotFound, "Clinic was not found.")
            : Result<ClinicDto>.Ok(mapper.Map<ClinicDto>(clinic));
    }

    public async Task<Result<ClinicDto>> CreateAsync(CreateClinicDto request, CancellationToken cancellationToken)
    {
        var existingClinic = await clinicRepository.GetByNameAsync(request.Name.Trim(), cancellationToken);
        if (existingClinic is not null)
        {
            return Result<ClinicDto>.Fail(ResultStatus.Conflict, "A clinic with this name already exists.");
        }

        var clinic = new Clinic
        {
            Name = request.Name.Trim(),
            Phone = request.Phone.Trim(),
            Email = request.Email.Trim().ToLowerInvariant(),
            Address = request.Address.Trim(),
            TimeZone = request.TimeZone.Trim(),
            SlotIntervalMinutes = request.SlotIntervalMinutes
        };

        await clinicRepository.AddAsync(clinic, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<ClinicDto>.Ok(mapper.Map<ClinicDto>(clinic), "Clinic created successfully.");
    }

    public async Task<Result<ClinicDto>> UpdateAsync(Guid id, UpdateClinicDto request, CancellationToken cancellationToken)
    {
        var clinic = await clinicRepository.GetByIdAsync(id, cancellationToken);
        if (clinic is null)
        {
            return Result<ClinicDto>.Fail(ResultStatus.NotFound, "Clinic was not found.");
        }

        var existingClinic = await clinicRepository.GetByNameAsync(request.Name.Trim(), cancellationToken);
        if (existingClinic is not null && existingClinic.Id != id)
        {
            return Result<ClinicDto>.Fail(ResultStatus.Conflict, "A clinic with this name already exists.");
        }

        clinic.Name = request.Name.Trim();
        clinic.Phone = request.Phone.Trim();
        clinic.Email = request.Email.Trim().ToLowerInvariant();
        clinic.Address = request.Address.Trim();
        clinic.TimeZone = request.TimeZone.Trim();
        clinic.SlotIntervalMinutes = request.SlotIntervalMinutes;

        clinicRepository.Update(clinic);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<ClinicDto>.Ok(mapper.Map<ClinicDto>(clinic), "Clinic updated successfully.");
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var clinic = await clinicRepository.GetByIdAsync(id, cancellationToken);
        if (clinic is null)
        {
            return Result.Fail(ResultStatus.NotFound, "Clinic was not found.");
        }

        clinic.IsDeleted = true;
        clinicRepository.Update(clinic);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Ok("Clinic deleted successfully.");
    }
}
