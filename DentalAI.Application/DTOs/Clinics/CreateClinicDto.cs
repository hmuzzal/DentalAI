namespace DentalAI.Application.DTOs.Clinics;

public sealed class CreateClinicDto
{
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string TimeZone { get; set; } = string.Empty;
    public int SlotIntervalMinutes { get; set; } = 15;
}
