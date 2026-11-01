namespace DentalAI.Application.DTOs.Users;

public sealed class UserProfileDto
{
    public Guid Id { get; set; }
    public Guid? ClinicId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string? ClinicName { get; set; }
    public string? ClinicTimeZone { get; set; }
    /// <summary>ISO 3166-1 alpha-2 country of the user's clinic; phone numbers without a country code are read as this.</summary>
    public string? ClinicCountry { get; set; }
}
