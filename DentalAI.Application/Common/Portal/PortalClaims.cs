namespace DentalAI.Application.Common.Portal;

/// <summary>What a patient portal access token carries. Patient tokens have their own audience and scheme.</summary>
public static class PortalClaims
{
    public const string PatientRole = "Patient";
    public const string PatientId = "patientId";
    /// <summary>Same claim name as staff tokens, so the tenant filter works unchanged.</summary>
    public const string ClinicId = "clinicId";
}
