using AutoMapper;
using DentalAI.Application.Common.Interfaces;
using DentalAI.Application.Common.Interfaces.Persistence;
using DentalAI.Application.Common.Models;
using DentalAI.Application.Common.Phone;
using DentalAI.Application.DTOs.Patients;
using DentalAI.Application.Interfaces;
using DentalAI.Domain.Entities;
using DentalAI.Domain.Enums;

namespace DentalAI.Application.Services;

public sealed class PatientService(
    IPatientRepository patientRepository,
    IClinicRepository clinicRepository,
    ICurrentUserService currentUserService,
    IDateTimeProvider dateTimeProvider,
    IUnitOfWork unitOfWork,
    IMapper mapper) : IPatientService
{
    public async Task<Result<IReadOnlyList<PatientDto>>> ListAsync(CancellationToken cancellationToken)
    {
        var effectiveClinicId = OwnClinicId;
        var patients = await patientRepository.ListAsync(effectiveClinicId, cancellationToken);
        return Result<IReadOnlyList<PatientDto>>.Ok(mapper.Map<IReadOnlyList<PatientDto>>(patients));
    }

    public async Task<Result<PagedResult<PatientDto>>> SearchAsync(PatientSearchQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, PatientSearchQuery.MaxPageSize);
        var (items, total) = await patientRepository.SearchAsync(
            OwnClinicId, query.Search, page, pageSize, cancellationToken);

        return Result<PagedResult<PatientDto>>.Ok(
            new PagedResult<PatientDto>(mapper.Map<IReadOnlyList<PatientDto>>(items), total, page, pageSize));
    }

    /// <summary>The full record, including insurance.</summary>
    public async Task<Result<PatientDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var patient = await patientRepository.GetByIdAsync(id, cancellationToken);
        return patient is null
            ? Result<PatientDto>.Fail(ResultStatus.NotFound, "Patient was not found.")
            : Result<PatientDto>.Ok(mapper.Map<PatientDto>(patient));
    }

    public async Task<Result<PatientDto>> CreateAsync(UpsertPatientRequest request, CancellationToken cancellationToken)
    {
        var clinicId = OwnClinicId;
        if (!clinicId.HasValue)
        {
            return Result<PatientDto>.Fail(ResultStatus.Forbidden, "Clinic context is required.");
        }

        var clinic = await clinicRepository.GetByIdAsync(clinicId.Value, cancellationToken);
        if (clinic is null)
        {
            return Result<PatientDto>.Fail(ResultStatus.NotFound, "Clinic was not found.");
        }

        if (!PhoneNumberRules.TryNormalize(request.Phone, clinic.Country, out var phone))
        {
            return InvalidPhone(clinic.Country);
        }

        var patient = new Patient { ClinicId = clinic.Id };
        Apply(patient, request, phone);
        if (request.Insurances is not null) SyncInsurance(patient, request.Insurances);

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

        var clinicId = patient.ClinicId;
        if (!currentUserService.CanAccessClinic(clinicId))
        {
            return Result<PatientDto>.Fail(ResultStatus.Forbidden, "You cannot modify patients for another clinic.");
        }

        var clinic = clinicId.HasValue ? await clinicRepository.GetByIdAsync(clinicId.Value, cancellationToken) : null;
        if (clinic is null)
        {
            return Result<PatientDto>.Fail(ResultStatus.NotFound, "Clinic was not found.");
        }

        if (!PhoneNumberRules.TryNormalize(request.Phone, clinic.Country, out var phone))
        {
            return InvalidPhone(clinic.Country);
        }

        // The patient is tracked, so changes are saved without Update() (which would mark new insurance rows as
        // modified instead of added).
        patient.ClinicId = clinic.Id;
        Apply(patient, request, phone);
        if (request.Insurances is not null) SyncInsurance(patient, request.Insurances);

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

        foreach (var insurance in patient.Insurances.ToList())
        {
            patientRepository.RemoveInsurance(insurance);
        }

        patientRepository.Remove(patient);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Ok("Patient deleted successfully.");
    }

    private void Apply(Patient patient, UpsertPatientRequest request, string phone)
    {
        patient.FirstName = request.FirstName.Trim();
        patient.LastName = request.LastName.Trim();
        patient.Phone = phone;
        patient.Email = Optional(request.Email)?.ToLowerInvariant();
        patient.DateOfBirth = request.DateOfBirth;
        patient.Gender = Optional(request.Gender);
        patient.Notes = Optional(request.Notes);

        patient.AddressLine1 = Optional(request.AddressLine1);
        patient.AddressLine2 = Optional(request.AddressLine2);
        patient.City = Optional(request.City);
        patient.State = Optional(request.State)?.ToUpperInvariant();
        patient.PostalCode = Optional(request.PostalCode)?.ToUpperInvariant();
        patient.PreferredContactMethod = request.PreferredContactMethod;

        // TCPA: keep a record of when consent was given or withdrawn.
        if (request.SmsOptIn && !patient.SmsOptIn)
        {
            patient.SmsOptInAtUtc = dateTimeProvider.UtcNow;
            patient.SmsOptOutAtUtc = null;
        }
        else if (!request.SmsOptIn && patient.SmsOptIn)
        {
            patient.SmsOptOutAtUtc = dateTimeProvider.UtcNow;
        }

        patient.SmsOptIn = request.SmsOptIn;
    }

    /// <summary>Makes the patient's policies match the request, matched by priority (updated in place when present).</summary>
    private void SyncInsurance(Patient patient, IReadOnlyList<PatientInsuranceRequest> requested)
    {
        // Soft-deleted at save time. Left in the collection: removing it would sever the required FK first.
        foreach (var existing in patient.Insurances.Where(i => !i.IsDeleted && requested.All(r => r.Priority != i.Priority)).ToList())
        {
            patientRepository.RemoveInsurance(existing);
        }

        foreach (var request in requested)
        {
            var policy = patient.Insurances.FirstOrDefault(i => !i.IsDeleted && i.Priority == request.Priority);
            if (policy is null)
            {
                policy = new PatientInsurance { ClinicId = patient.ClinicId, PatientId = patient.Id, Priority = request.Priority };
                patient.Insurances.Add(policy);
                patientRepository.AddInsurance(policy);
            }

            var self = request.RelationshipToPatient == InsuranceRelationship.Self;
            policy.ClinicId = patient.ClinicId;
            policy.CarrierName = request.CarrierName.Trim();
            policy.MemberId = request.MemberId.Trim();
            policy.GroupNumber = Optional(request.GroupNumber);
            policy.RelationshipToPatient = request.RelationshipToPatient;
            // When the patient holds the plan, the subscriber is the patient.
            policy.SubscriberName = self ? $"{patient.FirstName} {patient.LastName}".Trim() : request.SubscriberName!.Trim();
            policy.SubscriberDateOfBirth = self ? patient.DateOfBirth : request.SubscriberDateOfBirth;
        }
    }

    private static Result<PatientDto> InvalidPhone(string? country) =>
        Result<PatientDto>.Fail(ResultStatus.ValidationError, "The phone number is not valid.", [PhoneNumberRules.InvalidMessage(country)]);

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Clinic data is always the signed-in user's own clinic (platform admins have none).</summary>
    private Guid? OwnClinicId => currentUserService.ClinicId;
}
