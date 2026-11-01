using DentalAI.Application.Common.Interfaces;
using DentalAI.Application.Common.Interfaces.Persistence;
using DentalAI.Application.Common.Models;
using DentalAI.Application.DTOs.Scheduling;
using DentalAI.Application.Interfaces;
using DentalAI.Domain.Entities;

namespace DentalAI.Application.Services;

public sealed class SchedulingService(
    ISchedulingRepository schedulingRepository,
    IDoctorRepository doctorRepository,
    IServiceRepository serviceRepository,
    ICurrentUserService currentUserService,
    IDateTimeProvider dateTimeProvider,
    IUnitOfWork unitOfWork,
    SchedulingEngine engine) : ISchedulingService
{
    public async Task<Result<DoctorAvailabilityDto>> GetAvailabilityAsync(Guid doctorId, CancellationToken cancellationToken)
    {
        var doctor = await doctorRepository.GetByIdAsync(doctorId, cancellationToken);
        if (doctor is null || !currentUserService.CanAccessClinic(doctor.ClinicId))
            return Result<DoctorAvailabilityDto>.Fail(ResultStatus.NotFound, "Doctor was not found for the current clinic.");

        return Result<DoctorAvailabilityDto>.Ok(await MapAsync(doctorId, cancellationToken));
    }

    public async Task<Result<DoctorAvailabilityDto>> SaveAvailabilityAsync(Guid doctorId, SaveDoctorAvailabilityRequest request, CancellationToken cancellationToken)
    {
        var doctor = await doctorRepository.GetByIdAsync(doctorId, cancellationToken);
        if (doctor is null || !currentUserService.CanAccessClinic(doctor.ClinicId))
            return Result<DoctorAvailabilityDto>.Fail(ResultStatus.NotFound, "Doctor was not found for the current clinic.");

        var schedules = request.Schedules.Select(x => new DoctorSchedule
        {
            ClinicId = doctor.ClinicId, DoctorId = doctorId, DayOfWeek = x.DayOfWeek,
            StartTime = x.StartTime, EndTime = x.EndTime, IsWorking = x.IsWorking
        }).ToList();
        var breaks = request.Breaks.Select(x => new DoctorBreak
        {
            ClinicId = doctor.ClinicId, DoctorId = doctorId, DayOfWeek = x.DayOfWeek,
            StartTime = x.StartTime, EndTime = x.EndTime
        }).ToList();
        var leaves = request.Leaves.Select(x => new DoctorLeave
        {
            ClinicId = doctor.ClinicId, DoctorId = doctorId, StartDate = x.StartDate,
            EndDate = x.EndDate, Reason = x.Reason.Trim()
        }).ToList();

        await schedulingRepository.ReplaceAvailabilityAsync(doctorId, doctor.ClinicId, schedules, breaks, leaves, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<DoctorAvailabilityDto>.Ok(await MapAsync(doctorId, cancellationToken), "Doctor availability saved successfully.");
    }

    public async Task<Result<IReadOnlyList<AvailableSlotDto>>> GetAvailableSlotsAsync(Guid doctorId, DateOnly date, Guid serviceId, CancellationToken cancellationToken, Guid? excludeAppointmentId = null)
    {
        var service = await serviceRepository.GetByIdAsync(serviceId, cancellationToken);
        var snapshot = await schedulingRepository.GetSnapshotAsync(doctorId, date, cancellationToken, excludeAppointmentId);
        if (snapshot is null || !currentUserService.CanAccessClinic(snapshot.ClinicId))
            return Result<IReadOnlyList<AvailableSlotDto>>.Fail(ResultStatus.NotFound, "Doctor was not found for the current clinic.");
        if (service is null || service.ClinicId != snapshot.ClinicId || !service.IsActive)
            return Result<IReadOnlyList<AvailableSlotDto>>.Fail(ResultStatus.NotFound, "Service was not found or is inactive for the selected clinic.");

        return Result<IReadOnlyList<AvailableSlotDto>>.Ok(engine.GenerateSlots(snapshot, service.DurationMinutes, dateTimeProvider.UtcNow));
    }

    public async Task<Result> ValidateAppointmentAsync(Guid doctorId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken, Guid? excludeAppointmentId = null)
    {
        var doctor = await doctorRepository.GetByIdAsync(doctorId, cancellationToken);
        if (doctor is null || !currentUserService.CanAccessClinic(doctor.ClinicId))
            return Result.Fail(ResultStatus.NotFound, "Doctor was not found for the current clinic.");

        var timeZone = SchedulingEngine.ResolveTimeZone(doctor.Clinic?.TimeZone);
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(startUtc, DateTimeKind.Utc), timeZone));
        var snapshot = await schedulingRepository.GetSnapshotAsync(doctorId, localDate, cancellationToken, excludeAppointmentId);
        if (snapshot is null) return Result.Fail(ResultStatus.NotFound, "Doctor schedule was not found.");
        var errors = engine.Validate(snapshot, startUtc, endUtc);
        return errors.Count == 0
            ? Result.Ok()
            : Result.Fail(errors.Any(x => x.Contains("booked", StringComparison.OrdinalIgnoreCase)) ? ResultStatus.Conflict : ResultStatus.ValidationError,
                "The appointment cannot be booked.", errors);
    }

    private async Task<DoctorAvailabilityDto> MapAsync(Guid doctorId, CancellationToken cancellationToken)
    {
        var schedules = await schedulingRepository.GetSchedulesAsync(doctorId, cancellationToken);
        var breaks = await schedulingRepository.GetBreaksAsync(doctorId, cancellationToken);
        var leaves = await schedulingRepository.GetLeavesAsync(doctorId, cancellationToken);
        return new DoctorAvailabilityDto(doctorId,
            schedules.Select(x => new DoctorScheduleDto(x.Id, x.DayOfWeek, x.StartTime, x.EndTime, x.IsWorking)).ToList(),
            breaks.Select(x => new DoctorBreakDto(x.Id, x.DayOfWeek, x.StartTime, x.EndTime)).ToList(),
            leaves.Select(x => new DoctorLeaveDto(x.Id, x.StartDate, x.EndDate, x.Reason)).ToList());
    }
}
