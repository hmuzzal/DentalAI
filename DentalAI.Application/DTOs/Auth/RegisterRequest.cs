namespace DentalAI.Application.DTOs.Auth;

public sealed class RegisterRequest
{
    public string ClinicName { get; set; } = string.Empty;
    public string? ClinicEmail { get; set; }
    public string? ClinicPhone { get; set; }
    public string? ClinicAddress { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string AdminEmail { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
