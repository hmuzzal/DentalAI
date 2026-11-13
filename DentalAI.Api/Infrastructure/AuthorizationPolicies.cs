namespace DentalAI.Api.Infrastructure;

/// <summary>
/// Platform admins (SuperAdmin) run the platform: clinics and platform-wide counts. They have no access to any
/// clinic's patients, appointments, doctors or services (HIPAA minimum necessary); clinic staff do, for their own clinic.
/// Patients use the portal only, with tokens of their own audience and scheme.
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>Platform admins only: clinics and the platform overview.</summary>
    public const string SuperAdminOnly = nameof(SuperAdminOnly);
    /// <summary>A clinic's admins only (their own clinic).</summary>
    public const string ClinicAdminOnly = nameof(ClinicAdminOnly);
    /// <summary>Anyone working in a clinic (admins and receptionists), for that clinic's data.</summary>
    public const string ClinicStaff = nameof(ClinicStaff);
    /// <summary>A signed-in patient on the portal (portal token).</summary>
    public const string PortalPatient = nameof(PortalPatient);
}

/// <summary>Authentication schemes: staff tokens and patient portal tokens never validate as each other.</summary>
public static class AuthSchemes
{
    /// <summary>The default: forwards to <see cref="Staff"/> or <see cref="Portal"/> by the request path.</summary>
    public const string Selector = "StaffOrPortal";
    public const string Staff = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
    public const string Portal = "Portal";
    public const string PortalPathPrefix = "/api/portal/";

    public static bool IsPortalPath(PathString path) =>
        path.StartsWithSegments("/api/portal", StringComparison.OrdinalIgnoreCase);
}

public static class PortalRateLimits
{
    /// <summary>Sign-in, code and refresh calls, per client IP.</summary>
    public const string SignIn = "portal-sign-in";

    /// <summary>Messages to the AI assistant, per client IP: each one can mean several model calls.</summary>
    public const string Assistant = "portal-assistant";
}
