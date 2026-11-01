using DentalAI.Application.Common.Models;
using DentalAI.Application.DTOs.Appointments;

namespace DentalAI.Application.Interfaces;

public interface IAppointmentService
{
    Task<Result<IReadOnlyList<AppointmentDto>>> ListAsync(CancellationToken cancellationToken);
    Task<Result<AppointmentDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<AppointmentDto>> CreateAppointmentAsync(CreateAppointmentRequest request, CancellationToken cancellationToken);
    Task<Result<AppointmentDto>> RescheduleAsync(Guid id, RescheduleAppointmentRequest request, CancellationToken cancellationToken);
    Task<Result<AppointmentDto>> ConfirmAsync(Guid id, UpdateAppointmentStatusRequest request, CancellationToken cancellationToken);
    Task<Result<AppointmentDto>> CancelAsync(Guid id, UpdateAppointmentStatusRequest request, CancellationToken cancellationToken);
    Task<Result<AppointmentDto>> CompleteAsync(Guid id, UpdateAppointmentStatusRequest request, CancellationToken cancellationToken);
    Task<Result<AppointmentDto>> NoShowAsync(Guid id, UpdateAppointmentStatusRequest request, CancellationToken cancellationToken);
}
