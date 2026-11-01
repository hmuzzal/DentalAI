namespace DentalAI.Application.DTOs.Doctors;

public sealed class DoctorDto
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Specialty { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    /// <summary>IANA time zone of the doctor's clinic; slots and bookings for this doctor use it.</summary>
    public string? ClinicTimeZone { get; set; }
}
