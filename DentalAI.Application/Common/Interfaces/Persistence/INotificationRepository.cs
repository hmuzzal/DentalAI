using DentalAI.Domain.Entities;

namespace DentalAI.Application.Common.Interfaces.Persistence;

/// <summary>An appointment with everything a message about it needs (loaded without the tenant filter).</summary>
public sealed record AppointmentForMessage(
    Appointment Appointment,
    Patient Patient,
    Doctor Doctor,
    Service Service,
    Clinic Clinic);

/// <summary>
/// Notification settings and the message log/outbox. Methods marked "system" run in the background job (no signed-in
/// user, so no tenant filter) and must scope themselves; the others go through the tenant filter.
/// </summary>
public interface INotificationRepository
{
    // ---- Signed-in clinic ----
    Task<ClinicNotificationSettings?> GetSettingsAsync(CancellationToken cancellationToken);
    Task<(IReadOnlyList<OutboundMessage> Items, int TotalCount)> ListAsync(
        MessageStatus? status, MessageChannel? channel, Guid? patientId, Guid? appointmentId, int page, int pageSize, CancellationToken cancellationToken);
    /// <summary>Full names of the signed-in clinic's patients, by ID (deleted patients are left out).</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetPatientNamesAsync(IReadOnlyCollection<Guid> patientIds, CancellationToken cancellationToken);

    // ---- System (any clinic) ----
    Task<ClinicNotificationSettings?> GetSettingsForClinicAsync(Guid clinicId, CancellationToken cancellationToken);
    void AddSettings(ClinicNotificationSettings settings);
    Task<AppointmentForMessage?> GetAppointmentForMessageAsync(Guid appointmentId, CancellationToken cancellationToken);
    /// <summary>Pending or confirmed appointments starting in [fromUtc, toUtc), in clinics with reminders on.</summary>
    Task<IReadOnlyList<AppointmentForMessage>> GetReminderCandidatesAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);
    Task<bool> ReminderExistsAsync(Guid appointmentId, int reminderHours, CancellationToken cancellationToken);
    /// <summary>Withdraws pending messages for an appointment (e.g. reminders after it's cancelled or moved).</summary>
    Task CancelPendingForAppointmentAsync(Guid appointmentId, MessageKind[] kinds, CancellationToken cancellationToken);
    void AddMessage(OutboundMessage message);

    /// <summary>Messages stuck in Sending since before <paramref name="olderThanUtc"/> go back to Pending.</summary>
    Task<int> ReleaseStaleClaimsAsync(DateTime olderThanUtc, CancellationToken cancellationToken);
    /// <summary>Up to <paramref name="count"/> due Pending messages, tracked, marked Sending.</summary>
    Task<IReadOnlyList<OutboundMessage>> ClaimDueAsync(DateTime nowUtc, int count, CancellationToken cancellationToken);

    /// <summary>Patients (any clinic) with this E.164 phone, tracked, for STOP / START replies.</summary>
    Task<IReadOnlyList<Patient>> FindPatientsByPhoneAsync(string e164, CancellationToken cancellationToken);
    /// <summary>The latest sent appointment text to this phone since <paramref name="sinceUtc"/>, for "C" replies.</summary>
    Task<OutboundMessage?> LatestAppointmentTextToAsync(string e164, DateTime sinceUtc, CancellationToken cancellationToken);
    Task<Appointment?> GetAppointmentTrackedAsync(Guid appointmentId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
