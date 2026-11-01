namespace DentalAI.Application.DTOs.Doctors;

public sealed class UpdateDoctorDto
{
    /// <summary>Super admins only: the doctor's clinic. Clinic users always get their own clinic.</summary>
    public Guid? ClinicId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Specialty { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
