namespace DentalAI.Application.DTOs.Appointments;

public sealed class RescheduleAppointmentRequest
{
    /// <summary>New start time (UTC), e.g. an available slot's StartTimeUtc.</summary>
    public DateTime StartTime { get; set; }

    /// <summary>Optional; replaces the appointment's notes when provided.</summary>
    public string? Notes { get; set; }
}
