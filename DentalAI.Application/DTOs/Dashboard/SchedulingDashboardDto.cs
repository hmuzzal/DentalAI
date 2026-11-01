namespace DentalAI.Application.DTOs.Dashboard;

public sealed record RevenuePointDto(string Period, decimal Revenue);
public sealed record SchedulingDashboardDto(
    int TodayAppointments,
    int UpcomingAppointments,
    int CompletedToday,
    int CancelledToday,
    int NoShowsToday,
    IReadOnlyList<RevenuePointDto> RevenueByDay,
    IReadOnlyList<RevenuePointDto> RevenueByMonth);
