using AutoMapper;
using DentalAI.Application.Common.Interfaces;
using DentalAI.Application.Common.Interfaces.Persistence;
using DentalAI.Application.Common.Models;
using DentalAI.Application.DTOs.Appointments;
using DentalAI.Application.Interfaces;
using DentalAI.Domain.Entities;
using DentalAI.Domain.Enums;

namespace DentalAI.Application.Services;

public sealed class AppointmentService(
    IAppointmentRepository appointmentRepository,
    IDoctorRepository doctorRepository,
    IPatientRepository patientRepository,
    IServiceRepository serviceRepository,
    ISchedulingService schedulingService,
    ICurrentUserService currentUserService,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider,
    IMapper mapper) : IAppointmentService
{
    public async Task<Result<IReadOnlyList<AppointmentDto>>> ListAsync(CancellationToken cancellationToken)
    {
        var clinicId = currentUserService.IsSuperAdmin ? null : currentUserService.ClinicId;
        var appointments = await appointmentRepository.ListAsync(clinicId, cancellationToken);
        return Result<IReadOnlyList<AppointmentDto>>.Ok(mapper.Map<IReadOnlyList<AppointmentDto>>(appointments));
    }

    public async Task<Result<AppointmentDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var appointment = await appointmentRepository.GetByIdAsync(id, cancellationToken);
        return appointment is null
            ? Result<AppointmentDto>.Fail(ResultStatus.NotFound, "Appointment was not found.")
            : Result<AppointmentDto>.Ok(mapper.Map<AppointmentDto>(appointment));
    }

    public async Task<Result<AppointmentDto>> CreateAppointmentAsync(CreateAppointmentRequest request, CancellationToken cancellationToken)
    {
        if (request.StartTime <= dateTimeProvider.UtcNow)
        {
            return Result<AppointmentDto>.Fail(ResultStatus.ValidationError, "Appointments cannot be booked in the past.");
        }

        var doctor = await doctorRepository.GetByIdAsync(request.DoctorId, cancellationToken);
        if (doctor is null || !currentUserService.CanAccessClinic(doctor.ClinicId))
        {
            return Result<AppointmentDto>.Fail(ResultStatus.NotFound, "Doctor was not found for the current clinic.");
        }

        var patient = await patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
        if (patient is null || patient.ClinicId != doctor.ClinicId)
        {
            return Result<AppointmentDto>.Fail(ResultStatus.NotFound, "Patient was not found for the selected clinic.");
        }

        var service = await serviceRepository.GetByIdAsync(request.ServiceId, cancellationToken);
        if (service is null || service.ClinicId != doctor.ClinicId || !service.IsActive)
        {
            return Result<AppointmentDto>.Fail(ResultStatus.NotFound, "Service was not found or is inactive for the selected clinic.");
        }

        var endTime = request.StartTime.AddMinutes(service.DurationMinutes);

        // Check-then-insert under the doctor's schedule lock, so concurrent requests can't both take the slot.
        return await unitOfWork.RunWithDoctorScheduleLockAsync(doctor.Id, async ct =>
        {
            var schedulingValidation = await schedulingService.ValidateAppointmentAsync(doctor.Id, request.StartTime, endTime, ct);
            if (!schedulingValidation.Success)
                return Result<AppointmentDto>.Fail(schedulingValidation.Status, schedulingValidation.Message, schedulingValidation.Errors);

            var appointment = new Appointment
            {
                ClinicId = doctor.ClinicId,
                DoctorId = doctor.Id,
                PatientId = patient.Id,
                ServiceId = service.Id,
                StartTime = request.StartTime,
                EndTime = endTime,
                Notes = request.Notes?.Trim(),
                Doctor = doctor,
                Patient = patient,
                Service = service
            };

            await appointmentRepository.AddAsync(appointment, ct);
            await unitOfWork.SaveChangesAsync(ct);

            return Result<AppointmentDto>.Ok(mapper.Map<AppointmentDto>(appointment), "Appointment created successfully.");
        }, cancellationToken);
    }

    /// <summary>
    /// Moves an appointment to a new start time with the same doctor and service. The length follows the
    /// service's current duration, and the status is kept.
    /// </summary>
    public async Task<Result<AppointmentDto>> RescheduleAsync(Guid id, RescheduleAppointmentRequest request, CancellationToken cancellationToken)
    {
        if (request.StartTime <= dateTimeProvider.UtcNow)
        {
            return Result<AppointmentDto>.Fail(ResultStatus.ValidationError, "Appointments cannot be moved into the past.");
        }

        var appointment = await appointmentRepository.GetByIdAsync(id, cancellationToken);
        if (appointment is null || !currentUserService.CanAccessClinic(appointment.ClinicId))
        {
            return Result<AppointmentDto>.Fail(ResultStatus.NotFound, "Appointment was not found.");
        }

        if (appointment.Status is not (AppointmentStatus.Pending or AppointmentStatus.Confirmed))
        {
            return Result<AppointmentDto>.Fail(ResultStatus.Conflict, "Only pending or confirmed appointments can be rescheduled.");
        }

        var durationMinutes = appointment.Service?.DurationMinutes
            ?? (int)(appointment.EndTime - appointment.StartTime).TotalMinutes;
        var endTime = request.StartTime.AddMinutes(durationMinutes);

        return await unitOfWork.RunWithDoctorScheduleLockAsync(appointment.DoctorId, async ct =>
        {
            // Ignore the appointment itself, or it would block its own neighbouring slots.
            var schedulingValidation = await schedulingService.ValidateAppointmentAsync(
                appointment.DoctorId, request.StartTime, endTime, ct, excludeAppointmentId: appointment.Id);
            if (!schedulingValidation.Success)
                return Result<AppointmentDto>.Fail(schedulingValidation.Status, schedulingValidation.Message, schedulingValidation.Errors);

            appointment.StartTime = request.StartTime;
            appointment.EndTime = endTime;
            if (!string.IsNullOrWhiteSpace(request.Notes))
            {
                appointment.Notes = request.Notes.Trim();
            }

            await unitOfWork.SaveChangesAsync(ct);
            return Result<AppointmentDto>.Ok(mapper.Map<AppointmentDto>(appointment), "Appointment rescheduled successfully.");
        }, cancellationToken);
    }

    public Task<Result<AppointmentDto>> ConfirmAsync(Guid id, UpdateAppointmentStatusRequest request, CancellationToken cancellationToken) =>
        UpdateStatusAsync(id, AppointmentStatus.Confirmed, request, cancellationToken);

    public Task<Result<AppointmentDto>> CancelAsync(Guid id, UpdateAppointmentStatusRequest request, CancellationToken cancellationToken) =>
        UpdateStatusAsync(id, AppointmentStatus.Cancelled, request, cancellationToken);

    public Task<Result<AppointmentDto>> CompleteAsync(Guid id, UpdateAppointmentStatusRequest request, CancellationToken cancellationToken) =>
        UpdateStatusAsync(id, AppointmentStatus.Completed, request, cancellationToken);

    public Task<Result<AppointmentDto>> NoShowAsync(Guid id, UpdateAppointmentStatusRequest request, CancellationToken cancellationToken) =>
        UpdateStatusAsync(id, AppointmentStatus.NoShow, request, cancellationToken);

    private async Task<Result<AppointmentDto>> UpdateStatusAsync(
        Guid id,
        AppointmentStatus newStatus,
        UpdateAppointmentStatusRequest request,
        CancellationToken cancellationToken)
    {
        var appointment = await appointmentRepository.GetByIdAsync(id, cancellationToken);
        if (appointment is null)
        {
            return Result<AppointmentDto>.Fail(ResultStatus.NotFound, "Appointment was not found.");
        }

        var transitionError = ValidateStatusTransition(appointment.Status, newStatus);
        if (transitionError is not null)
        {
            return Result<AppointmentDto>.Fail(ResultStatus.Conflict, transitionError);
        }

        appointment.Status = newStatus;
        if (!string.IsNullOrWhiteSpace(request.Notes))
        {
            appointment.Notes = request.Notes.Trim();
        }

        appointmentRepository.Update(appointment);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<AppointmentDto>.Ok(mapper.Map<AppointmentDto>(appointment), "Appointment status updated successfully.");
    }

    private static string? ValidateStatusTransition(AppointmentStatus currentStatus, AppointmentStatus nextStatus)
    {
        if (currentStatus == nextStatus)
        {
            return null;
        }

        if (currentStatus is AppointmentStatus.Cancelled or AppointmentStatus.Completed or AppointmentStatus.NoShow)
        {
            return "Completed, cancelled, or no-show appointments cannot be changed.";
        }

        if (nextStatus == AppointmentStatus.Completed && currentStatus != AppointmentStatus.Confirmed)
        {
            return "Only confirmed appointments can be completed.";
        }

        return null;
    }
}
