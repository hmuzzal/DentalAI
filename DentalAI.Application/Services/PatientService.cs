using AutoMapper;
using DentalAI.Application.Common.Interfaces;
using DentalAI.Application.Common.Interfaces.Persistence;
using DentalAI.Application.Common.Models;
using DentalAI.Application.DTOs.Patients;
using DentalAI.Application.Interfaces;
using DentalAI.Domain.Entities;

namespace DentalAI.Application.Services;

public sealed class PatientService(
    IPatientRepository patientRepository,
    ICurrentUserService currentUserService,
    IUnitOfWork unitOfWork,
    IMapper mapper) : IPatientService
{
    public async Task<Result<IReadOnlyList<PatientDto>>> ListAsync(Guid? clinicId, CancellationToken cancellationToken)
    {
        var effectiveClinicId = ResolveClinicId(clinicId);
        var patients = await patientRepository.ListAsync(effectiveClinicId, cancellationToken);
        return Result<IReadOnlyList<PatientDto>>.Ok(mapper.Map<IReadOnlyList<PatientDto>>(patients));
    }

    public async Task<Result<PagedResult<PatientDto>>> SearchAsync(PatientSearchQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, PatientSearchQuery.MaxPageSize);
        var (items, total) = await patientRepository.SearchAsync(
            ResolveClinicId(query.ClinicId), query.Search, page, pageSize, cancellationToken);

        return Result<PagedResult<PatientDto>>.Ok(
            new PagedResult<PatientDto>(mapper.Map<IReadOnlyList<PatientDto>>(items), total, page, pageSize));
    }

    public async Task<Result<PatientDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var patient = await patientRepository.GetByIdAsync(id, cancellationToken);
        return patient is null
            ? Result<PatientDto>.Fail(ResultStatus.NotFound, "Patient was not found.")
            : Result<PatientDto>.Ok(mapper.Map<PatientDto>(patient));
    }

    public async Task<Result<PatientDto>> CreateAsync(UpsertPatientRequest request, CancellationToken cancellationToken)
    {
        var clinicId = ResolveClinicId(request.ClinicId);
        if (!clinicId.HasValue)
        {
            return Result<PatientDto>.Fail(ResultStatus.Forbidden, "Clinic context is required.");
        }

        var patient = new Patient
        {
            ClinicId = clinicId.Value,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Phone = request.Phone.Trim(),
            Email = request.Email?.Trim().ToLowerInvariant(),
            DateOfBirth = request.DateOfBirth,
            Gender = request.Gender?.Trim(),
            Notes = request.Notes?.Trim()
        };

        await patientRepository.AddAsync(patient, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<PatientDto>.Ok(mapper.Map<PatientDto>(patient), "Patient created successfully.");
    }

    public async Task<Result<PatientDto>> UpdateAsync(Guid id, UpsertPatientRequest request, CancellationToken cancellationToken)
    {
        var patient = await patientRepository.GetByIdAsync(id, cancellationToken);
        if (patient is null)
        {
            return Result<PatientDto>.Fail(ResultStatus.NotFound, "Patient was not found.");
        }

        var clinicId = ResolveClinicId(request.ClinicId) ?? patient.ClinicId;
        if (!currentUserService.CanAccessClinic(clinicId))
        {
            return Result<PatientDto>.Fail(ResultStatus.Forbidden, "You cannot modify patients for another clinic.");
        }

        patient.ClinicId = clinicId;
        patient.FirstName = request.FirstName.Trim();
        patient.LastName = request.LastName.Trim();
        patient.Phone = request.Phone.Trim();
        patient.Email = request.Email?.Trim().ToLowerInvariant();
        patient.DateOfBirth = request.DateOfBirth;
        patient.Gender = request.Gender?.Trim();
        patient.Notes = request.Notes?.Trim();

        patientRepository.Update(patient);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<PatientDto>.Ok(mapper.Map<PatientDto>(patient), "Patient updated successfully.");
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var patient = await patientRepository.GetByIdAsync(id, cancellationToken);
        if (patient is null)
        {
            return Result.Fail(ResultStatus.NotFound, "Patient was not found.");
        }

        patientRepository.Remove(patient);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Ok("Patient deleted successfully.");
    }

    private Guid? ResolveClinicId(Guid? requestedClinicId)
    {
        if (currentUserService.IsSuperAdmin)
        {
            return requestedClinicId;
        }

        return currentUserService.ClinicId;
    }
}
