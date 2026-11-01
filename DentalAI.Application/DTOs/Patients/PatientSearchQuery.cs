namespace DentalAI.Application.DTOs.Patients;

/// <summary>GET /patients/search. Matches name, phone or email; results ordered by last name, then first name.</summary>
public sealed class PatientSearchQuery
{
    public const int MaxPageSize = 100;

    public Guid? ClinicId { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
