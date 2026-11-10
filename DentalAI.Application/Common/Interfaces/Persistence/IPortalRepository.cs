using DentalAI.Domain.Entities;

namespace DentalAI.Application.Common.Interfaces.Persistence;

/// <summary>
/// Patient portal data. The "anonymous" methods run before anyone is signed in, so they skip the tenant filter and
/// scope by clinic ID themselves (and filter soft-deleted rows). The rest use the signed-in clinic (tenant filter).
/// </summary>
public interface IPortalRepository
{
    // ---- Anonymous (explicit clinic) ----

    /// <summary>The portal and its clinic, when the clinic exists. Whether the portal is on is up to the caller.</summary>
    Task<(ClinicPortalSettings Settings, Clinic Clinic)?> GetBySlugAsync(string slug, CancellationToken cancellationToken);
    Task<bool> SlugTakenAsync(string slug, Guid exceptClinicId, CancellationToken cancellationToken);
    /// <summary>Patients of the clinic with this email (lowercase) or phone (E.164).</summary>
    Task<IReadOnlyList<Patient>> FindPatientsByContactAsync(Guid clinicId, string contact, MessageChannel channel, CancellationToken cancellationToken);
    Task<int> CountCodesSinceAsync(Guid clinicId, string contact, DateTime sinceUtc, CancellationToken cancellationToken);
    /// <summary>The newest code for the contact that isn't verified, expired or out of attempts. Tracked.</summary>
    Task<PortalSignInCode?> GetOpenCodeAsync(Guid clinicId, string contact, DateTime nowUtc, int maxAttempts, CancellationToken cancellationToken);
    /// <summary>Tracked.</summary>
    Task<PortalSignInCode?> GetByTicketHashAsync(Guid clinicId, string ticketHash, CancellationToken cancellationToken);
    /// <summary>Tracked, with the patient (even when deleted, so the caller can refuse it).</summary>
    Task<PatientPortalSession?> GetSessionAsync(string tokenHash, CancellationToken cancellationToken);
    Task<ClinicPortalSettings?> GetSettingsForClinicAsync(Guid clinicId, CancellationToken cancellationToken);
    /// <summary>Null when the clinic doesn't exist or is deleted.</summary>
    Task<Clinic?> GetClinicAsync(Guid clinicId, CancellationToken cancellationToken);

    void AddCode(PortalSignInCode code);
    void AddSession(PatientPortalSession session);
    void AddPatient(Patient patient);
    void AddSettings(ClinicPortalSettings settings);

    // ---- Signed-in clinic (tenant filter) ----

    /// <summary>Tracked.</summary>
    Task<ClinicPortalSettings?> GetSettingsAsync(CancellationToken cancellationToken);
    /// <summary>The patient's appointments, newest first, with doctor and service.</summary>
    Task<IReadOnlyList<Appointment>> ListAppointmentsAsync(Guid patientId, int take, CancellationToken cancellationToken);
    /// <summary>Pending or confirmed appointments of the patient that start after <paramref name="nowUtc"/>.</summary>
    Task<int> CountUpcomingAsync(Guid patientId, DateTime nowUtc, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
