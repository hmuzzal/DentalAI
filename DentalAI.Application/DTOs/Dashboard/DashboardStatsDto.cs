namespace DentalAI.Application.DTOs.Dashboard;

public sealed class DashboardStatsDto
{
    public int TodayAppointments { get; set; }
    public int UpcomingAppointments { get; set; }
    public int TotalDoctors { get; set; }
    public int TotalPatients { get; set; }
    public int TotalServices { get; set; }
}
