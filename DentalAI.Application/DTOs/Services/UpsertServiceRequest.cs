namespace DentalAI.Application.DTOs.Services;

public sealed class UpsertServiceRequest
{
    /// <summary>Super admins only: the clinic to create the service in. Ignored for clinic users and on update.</summary>
    public Guid? ClinicId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int DurationMinutes { get; set; }
    public bool IsActive { get; set; } = true;
}
