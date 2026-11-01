using DentalAI.Application.Common.Models;
using DentalAI.Application.DTOs.Scheduling;

namespace DentalAI.Application.Interfaces;

public interface ISchedulingService
{
    Task<Result<DoctorAvailabilityDto>> GetAvailabilityAsync(Guid doctorId, CancellationToken cancellationToken);
    Task<Result<DoctorAvailabilityDto>> SaveAvailabilityAsync(Guid doctorId, SaveDoctorAvailabilityRequest request, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<AvailableSlotDto>>> GetAvailableSlotsAsync(Guid doctorId, DateOnly date, Guid serviceId, CancellationToken cancellationToken);
    Task<Result> ValidateAppointmentAsync(Guid doctorId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken);
}
