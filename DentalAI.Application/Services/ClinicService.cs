using DentalAI.Application.Common.Interfaces;
using DentalAI.Application.Mappings;
using DentalAI.Application.Common.Interfaces.Persistence;
using DentalAI.Application.Common.Models;
using DentalAI.Application.Common.Phone;
using DentalAI.Application.DTOs.Clinics;
using DentalAI.Application.Interfaces;
using DentalAI.Domain.Entities;

namespace DentalAI.Application.Services;

public sealed class ClinicService(
    IClinicRepository clinicRepository,
    IUserRepository userRepository,
    IStaffAccessCache staffAccess,
    IUnitOfWork unitOfWork) : IClinicService
{
    public async Task<Result<IReadOnlyList<ClinicDto>>> ListAsync(CancellationToken cancellationToken)
    {
        var clinics = await clinicRepository.ListAsync(cancellationToken);
        return Result<IReadOnlyList<ClinicDto>>.Ok(clinics.Select(x => x.ToDto()).ToList());
    }

    public async Task<Result<ClinicDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var clinic = await clinicRepository.GetByIdAsync(id, cancellationToken);
        return clinic is null
            ? Result<ClinicDto>.Fail(ResultStatus.NotFound, "Clinic was not found.")
            : Result<ClinicDto>.Ok(clinic.ToDto());
    }

    public async Task<Result<ClinicDto>> CreateAsync(CreateClinicDto request, CancellationToken cancellationToken)
    {
        var existingClinic = await clinicRepository.GetByNameAsync(request.Name.Trim(), cancellationToken);
        if (existingClinic is not null)
        {
            return Result<ClinicDto>.Fail(ResultStatus.Conflict, "A clinic with this name already exists.");
        }

        var country = request.Country.Trim().ToUpperInvariant();
        if (!PhoneNumberRules.TryNormalize(request.Phone, country, out var phone))
        {
            return Result<ClinicDto>.Fail(ResultStatus.ValidationError, "The phone number is not valid.", [PhoneNumberRules.InvalidMessage(country)]);
        }

        var clinic = new Clinic
        {
            Name = request.Name.Trim(),
            Phone = phone,
            Country = country,
            Email = request.Email.Trim().ToLowerInvariant(),
            Address = request.Address.Trim(),
            TimeZone = request.TimeZone.Trim(),
            SlotIntervalMinutes = request.SlotIntervalMinutes
        };

        await clinicRepository.AddAsync(clinic, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<ClinicDto>.Ok(clinic.ToDto(), "Clinic created successfully.");
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

        var country = request.Country.Trim().ToUpperInvariant();
        if (!PhoneNumberRules.TryNormalize(request.Phone, country, out var phone))
        {
            return Result<ClinicDto>.Fail(ResultStatus.ValidationError, "The phone number is not valid.", [PhoneNumberRules.InvalidMessage(country)]);
        }

        clinic.Name = request.Name.Trim();
        clinic.Phone = phone;
        clinic.Country = country;
        clinic.Email = request.Email.Trim().ToLowerInvariant();
        clinic.Address = request.Address.Trim();
        clinic.TimeZone = request.TimeZone.Trim();
        clinic.SlotIntervalMinutes = request.SlotIntervalMinutes;

        clinicRepository.Update(clinic);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<ClinicDto>.Ok(clinic.ToDto(), "Clinic updated successfully.");
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var clinic = await clinicRepository.GetByIdAsync(id, cancellationToken);
        if (clinic is null)
        {
            return Result.Fail(ResultStatus.NotFound, "Clinic was not found.");
        }

        // Close the clinic and lock out its staff: deactivate every account and end their sessions. (Sign-in also
        // refuses staff of a deleted clinic, so this holds even for accounts added later by mistake.)
        var staff = await userRepository.ListByClinicAsync(clinic.Id, cancellationToken);
        foreach (var user in staff)
        {
            user.IsActive = false;
            user.RefreshToken = null;
            user.RefreshTokenExpiry = null;
            user.InviteTokenHash = null;
            user.InviteExpiresAtUtc = null;
        }

        clinic.IsDeleted = true;
        clinicRepository.Update(clinic);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        foreach (var user in staff) staffAccess.Forget(user.Id);

        return Result.Ok("Clinic deleted successfully.");
    }
}
