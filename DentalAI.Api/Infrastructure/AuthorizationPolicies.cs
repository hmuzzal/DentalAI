namespace DentalAI.Api.Infrastructure;

/// <summary>
/// Platform admins (SuperAdmin) run the platform: clinics and platform-wide counts. They have no access to any
/// clinic's patients, appointments, doctors or services (HIPAA minimum necessary); clinic staff do, for their own clinic.
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>Platform admins only: clinics and the platform overview.</summary>
    public const string SuperAdminOnly = nameof(SuperAdminOnly);
    /// <summary>A clinic's admins only (their own clinic).</summary>
    public const string ClinicAdminOnly = nameof(ClinicAdminOnly);
    /// <summary>Anyone working in a clinic (admins and receptionists), for that clinic's data.</summary>
    public const string ClinicStaff = nameof(ClinicStaff);
}
