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
    ISchedulingRepository schedulingRepository,
    IDateTimeProvider dateTimeProvider) : IDashboardService
{
    public async Task<Result<DashboardStatsDto>> GetStatsAsync(CancellationToken cancellationToken)
    {
        var utcNow = dateTimeProvider.UtcNow;

        var doctors = await doctorRepository.ListAsync(null, cancellationToken);
        var services = await serviceRepository.ListAsync(cancellationToken);
        var totalPatients = await patientRepository.CountAsync(cancellationToken);
        var todayAppointments = (await GetTodayWindowRowsAsync(utcNow, cancellationToken))
            .Count(row => DashboardCalculator.IsToday(row, utcNow));
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

    public async Task<Result<SchedulingDashboardDto>> GetSchedulingStatsAsync(CancellationToken cancellationToken)
    {
        // "Today" and revenue days/months follow each appointment's clinic calendar, not UTC.
        var utcNow = dateTimeProvider.UtcNow;
        var todayRows = await GetTodayWindowRowsAsync(utcNow, cancellationToken);
        var (revenueFrom, revenueTo) = DashboardCalculator.RevenueWindowUtc(utcNow);
        var completedRows = await schedulingRepository.GetDashboardRowsAsync(revenueFrom, revenueTo, completedOnly: true, cancellationToken);
        var upcoming = await appointmentRepository.CountUpcomingAsync(utcNow, cancellationToken);

        return Result<SchedulingDashboardDto>.Ok(DashboardCalculator.Build(todayRows, completedRows, upcoming, utcNow));
    }

    private Task<IReadOnlyList<DashboardAppointmentRow>> GetTodayWindowRowsAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        var (fromUtc, toUtc) = DashboardCalculator.TodayWindowUtc(utcNow);
        return schedulingRepository.GetDashboardRowsAsync(fromUtc, toUtc, completedOnly: false, cancellationToken);
    }
}
