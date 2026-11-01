namespace DentalAI.Application.DTOs.Clinics;

public sealed class UpdateClinicDto
{
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string TimeZone { get; set; } = string.Empty;
    /// <summary>ISO 3166-1 alpha-2, e.g. "US". Numbers typed without a country code are read in this country.</summary>
    public string Country { get; set; } = "US";
    public int SlotIntervalMinutes { get; set; } = 15;
}
