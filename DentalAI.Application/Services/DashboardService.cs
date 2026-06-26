using DentalAI.Application.Common.Interfaces;
using DentalAI.Application.Common.Interfaces.Persistence;
using DentalAI.Application.Common.Models;
using DentalAI.Application.DTOs.Dashboard;
using DentalAI.Application.Interfaces;

namespace DentalAI.Application.Services;

public sealed class DashboardService(
    IAppointmentRepository appointmentRepository,
    IDoctorRepository doctorRepository,
    IPatientRepository patientRepository,
    IServiceRepository serviceRepository,
    IDateTimeProvider dateTimeProvider) : IDashboardService
{
    public async Task<Result<DashboardStatsDto>> GetStatsAsync(CancellationToken cancellationToken)
    {
        var utcNow = dateTimeProvider.UtcNow;
        var dayStart = utcNow.Date;
        var dayEnd = dayStart.AddDays(1);

        var doctors = await doctorRepository.ListAsync(null, cancellationToken);
        var services = await serviceRepository.ListAsync(cancellationToken);
        var totalPatients = await patientRepository.CountAsync(cancellationToken);
        var todayAppointments = await appointmentRepository.CountTodayAsync(dayStart, dayEnd, cancellationToken);
        var upcomingAppointments = await appointmentRepository.CountUpcomingAsync(utcNow, cancellationToken);

        return Result<DashboardStatsDto>.Ok(new DashboardStatsDto
        {
            TodayAppointments = todayAppointments,
            UpcomingAppointments = upcomingAppointments,
            TotalDoctors = doctors.Count,
            TotalPatients = totalPatients,
            TotalServices = services.Count
        });
    }
}
