namespace DentalAI.Application.DTOs.Platform;

/// <summary>
/// What platform admins see: counts only. Never patient, appointment or staff details (HIPAA minimum necessary).
/// </summary>
public sealed record PlatformOverviewDto(
    int Clinics,
    int ActiveUsers,
    int Doctors,
    int Patients,
    int AppointmentsLast30Days,
    int UpcomingAppointments,
    IReadOnlyList<ClinicUsageDto> ClinicUsage);

public sealed record ClinicUsageDto(
    Guid ClinicId,
    string Name,
    string Country,
    string TimeZone,
    DateTime CreatedDate,
    int ActiveUsers,
    int Doctors,
    int Patients,
    int AppointmentsLast30Days,
    int UpcomingAppointments);
