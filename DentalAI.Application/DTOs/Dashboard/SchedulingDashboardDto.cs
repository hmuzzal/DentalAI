namespace DentalAI.Application.DTOs.Dashboard;

public sealed record RevenuePointDto(string Period, decimal Revenue);

/// <param name="RevenueByDay">Null for receptionists: practice money is for clinic admins.</param>
/// <param name="RevenueByMonth">Null for receptionists, as above.</param>
public sealed record SchedulingDashboardDto(
    int TodayAppointments,
    int UpcomingAppointments,
    int CompletedToday,
    int CancelledToday,
    int NoShowsToday,
    IReadOnlyList<RevenuePointDto>? RevenueByDay,
    IReadOnlyList<RevenuePointDto>? RevenueByMonth);
