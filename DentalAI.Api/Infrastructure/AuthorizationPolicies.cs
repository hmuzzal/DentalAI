namespace DentalAI.Api.Infrastructure;

public static class AuthorizationPolicies
{
    public const string SuperAdminOnly = nameof(SuperAdminOnly);
    public const string ClinicAdminOnly = nameof(ClinicAdminOnly);
    public const string ReceptionistAccess = nameof(ReceptionistAccess);
}
