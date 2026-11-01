using AutoMapper;
using DentalAI.Application.Common.Interfaces;
using DentalAI.Application.Common.Interfaces.Persistence;
using DentalAI.Application.Common.Models;
using DentalAI.Application.DTOs.Doctors;
using DentalAI.Application.Interfaces;
using DentalAI.Domain.Entities;

namespace DentalAI.Application.Services;

public sealed class DoctorService(
    IDoctorRepository doctorRepository,
    IClinicRepository clinicRepository,
    IAppointmentRepository appointmentRepository,
    IDateTimeProvider dateTimeProvider,
    ICurrentUserService currentUserService,
    IUnitOfWork unitOfWork,
    IMapper mapper) : IDoctorService
{
    public async Task<Result<IReadOnlyList<DoctorDto>>> ListAsync(Guid? clinicId, CancellationToken cancellationToken)
    {
        var effectiveClinicId = ResolveClinicId(clinicId);
        var doctors = await doctorRepository.ListAsync(effectiveClinicId, cancellationToken);
        return Result<IReadOnlyList<DoctorDto>>.Ok(mapper.Map<IReadOnlyList<DoctorDto>>(doctors));
    }

    public async Task<Result<DoctorDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var doctor = await doctorRepository.GetByIdAsync(id, cancellationToken);
        return doctor is null
            ? Result<DoctorDto>.Fail(ResultStatus.NotFound, "Doctor was not found.")
            : Result<DoctorDto>.Ok(mapper.Map<DoctorDto>(doctor));
    }

    public async Task<Result<DoctorDto>> CreateAsync(CreateDoctorDto request, CancellationToken cancellationToken)
    {
        var effectiveClinicId = ResolveClinicId(request.ClinicId);
        if (!effectiveClinicId.HasValue)
        {
            return Result<DoctorDto>.Fail(ResultStatus.Forbidden, "Clinic context is required.");
        }

        var clinic = await clinicRepository.GetByIdAsync(effectiveClinicId.Value, cancellationToken);
        if (clinic is null)
        {
            return Result<DoctorDto>.Fail(ResultStatus.NotFound, "Clinic was not found.");
        }

        var doctor = new Doctor
        {
            ClinicId = effectiveClinicId.Value,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Specialty = request.Specialty.Trim(),
            Phone = request.Phone.Trim(),
            Email = request.Email.Trim().ToLowerInvariant()
        };

        await doctorRepository.AddAsync(doctor, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<DoctorDto>.Ok(mapper.Map<DoctorDto>(doctor), "Doctor created successfully.");
    }

    public async Task<Result<DoctorDto>> UpdateAsync(Guid id, UpdateDoctorDto request, CancellationToken cancellationToken)
    {
        var doctor = await doctorRepository.GetByIdAsync(id, cancellationToken);
        if (doctor is null)
        {
            return Result<DoctorDto>.Fail(ResultStatus.NotFound, "Doctor was not found.");
        }

        var effectiveClinicId = ResolveClinicId(request.ClinicId);
        if (!effectiveClinicId.HasValue)
        {
            return Result<DoctorDto>.Fail(ResultStatus.Forbidden, "Clinic context is required.");
        }

        var clinic = await clinicRepository.GetByIdAsync(effectiveClinicId.Value, cancellationToken);
        if (clinic is null)
        {
            return Result<DoctorDto>.Fail(ResultStatus.NotFound, "Clinic was not found.");
        }

        doctor.ClinicId = effectiveClinicId.Value;
        doctor.FirstName = request.FirstName.Trim();
        doctor.LastName = request.LastName.Trim();
        doctor.Specialty = request.Specialty.Trim();
        doctor.Phone = request.Phone.Trim();
        doctor.Email = request.Email.Trim().ToLowerInvariant();

        doctorRepository.Update(doctor);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<DoctorDto>.Ok(mapper.Map<DoctorDto>(doctor), "Doctor updated successfully.");
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var doctor = await doctorRepository.GetByIdAsync(id, cancellationToken);
        if (doctor is null)
        {
            return Result.Fail(ResultStatus.NotFound, "Doctor was not found.");
        }

        var upcoming = await appointmentRepository.CountUpcomingForDoctorAsync(doctor.Id, dateTimeProvider.UtcNow, cancellationToken);
        if (upcoming > 0)
        {
            return Result.Fail(ResultStatus.Conflict,
                $"{doctor.FirstName} {doctor.LastName} has {upcoming} upcoming appointment{(upcoming == 1 ? "" : "s")}. Cancel or reschedule them before deleting this doctor.");
        }

        doctorRepository.Remove(doctor);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Ok("Doctor deleted successfully.");
    }

    private Guid? ResolveClinicId(Guid? requestedClinicId)
    {
        if (currentUserService.IsInRole(Domain.Enums.UserRole.SuperAdmin))
        {
            return requestedClinicId;
        }

        return currentUserService.ClinicId;
    }
}
