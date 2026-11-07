using DentalAI.Application.Common.Interfaces.Persistence;
using DentalAI.Domain.Entities;
using DentalAI.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DentalAI.Infrastructure.Persistence.Repositories;

/// <summary>
/// The "system" methods skip the tenant filter on purpose (the background job has no signed-in user) and therefore
/// filter soft-deleted rows and scope by ID themselves.
/// </summary>
public sealed class NotificationRepository(ApplicationDbContext dbContext) : INotificationRepository
{
    // ---- Signed-in clinic (tenant filter applies) ----

    public Task<ClinicNotificationSettings?> GetSettingsAsync(CancellationToken cancellationToken) =>
        dbContext.ClinicNotificationSettings.FirstOrDefaultAsync(cancellationToken);

    public async Task<(IReadOnlyList<OutboundMessage> Items, int TotalCount)> ListAsync(
        MessageStatus? status, MessageChannel? channel, Guid? patientId, Guid? appointmentId, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        var query = dbContext.OutboundMessages.AsNoTracking().AsQueryable();
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        if (channel.HasValue) query = query.Where(x => x.Channel == channel.Value);
        if (patientId.HasValue) query = query.Where(x => x.PatientId == patientId.Value);
        if (appointmentId.HasValue) query = query.Where(x => x.AppointmentId == appointmentId.Value);

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.CreatedDate)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetPatientNamesAsync(IReadOnlyCollection<Guid> patientIds, CancellationToken cancellationToken)
    {
        if (patientIds.Count == 0) return new Dictionary<Guid, string>();
        return await dbContext.Patients.AsNoTracking()
            .Where(p => patientIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => (p.FirstName + " " + p.LastName).Trim(), cancellationToken);
    }

    // ---- System ----

    public Task<ClinicNotificationSettings?> GetSettingsForClinicAsync(Guid clinicId, CancellationToken cancellationToken) =>
        dbContext.ClinicNotificationSettings.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.ClinicId == clinicId && !x.IsDeleted, cancellationToken);

    public void AddSettings(ClinicNotificationSettings settings) => dbContext.ClinicNotificationSettings.Add(settings);

    public async Task<AppointmentForMessage?> GetAppointmentForMessageAsync(Guid appointmentId, CancellationToken cancellationToken) =>
        (await MessageData(dbContext.Appointments.IgnoreQueryFilters().Where(a => a.Id == appointmentId && !a.IsDeleted))
            .ToListAsync(cancellationToken)).Select(ToData).FirstOrDefault();

    public async Task<IReadOnlyList<AppointmentForMessage>> GetReminderCandidatesAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken) =>
        (await MessageData(dbContext.Appointments.IgnoreQueryFilters()
                .Where(a => !a.IsDeleted && a.StartTime >= fromUtc && a.StartTime < toUtc
                    && (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed)))
            .ToListAsync(cancellationToken)).Select(ToData).ToList();

    public Task<bool> ReminderExistsAsync(Guid appointmentId, int reminderHours, CancellationToken cancellationToken) =>
        dbContext.OutboundMessages.IgnoreQueryFilters().AnyAsync(x =>
            x.AppointmentId == appointmentId && x.Kind == MessageKind.Reminder && x.ReminderHours == reminderHours
            && x.Status != MessageStatus.Cancelled && !x.IsDeleted, cancellationToken);

    public async Task CancelPendingForAppointmentAsync(Guid appointmentId, MessageKind[] kinds, CancellationToken cancellationToken)
    {
        var pending = await dbContext.OutboundMessages.IgnoreQueryFilters()
            .Where(x => x.AppointmentId == appointmentId && kinds.Contains(x.Kind) && x.Status == MessageStatus.Pending && !x.IsDeleted)
            .ToListAsync(cancellationToken);
        foreach (var message in pending)
        {
            message.Status = MessageStatus.Cancelled;
            message.LastError = "Withdrawn: the appointment changed.";
        }
    }

    public void AddMessage(OutboundMessage message) => dbContext.OutboundMessages.Add(message);

    public Task<int> ReleaseStaleClaimsAsync(DateTime olderThanUtc, CancellationToken cancellationToken) =>
        dbContext.OutboundMessages.IgnoreQueryFilters()
            .Where(x => x.Status == MessageStatus.Sending && x.ClaimedAtUtc < olderThanUtc)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, MessageStatus.Pending).SetProperty(x => x.ClaimedAtUtc, (DateTime?)null),
                cancellationToken);

    public async Task<IReadOnlyList<OutboundMessage>> ClaimDueAsync(DateTime nowUtc, int count, CancellationToken cancellationToken)
    {
        var due = await dbContext.OutboundMessages.IgnoreQueryFilters()
            .Where(x => x.Status == MessageStatus.Pending && x.NotBeforeUtc <= nowUtc && !x.IsDeleted)
            .OrderBy(x => x.NotBeforeUtc)
            .Take(count)
            .ToListAsync(cancellationToken);
        foreach (var message in due)
        {
            message.Status = MessageStatus.Sending;
            message.ClaimedAtUtc = nowUtc;
        }
        await dbContext.SaveChangesAsync(cancellationToken); // claim before calling providers
        return due;
    }

    public async Task<IReadOnlyList<Patient>> FindPatientsByPhoneAsync(string e164, CancellationToken cancellationToken) =>
        await dbContext.Patients.IgnoreQueryFilters().Where(p => p.Phone == e164 && !p.IsDeleted).ToListAsync(cancellationToken);

    public Task<OutboundMessage?> LatestAppointmentTextToAsync(string e164, DateTime sinceUtc, CancellationToken cancellationToken) =>
        dbContext.OutboundMessages.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.To == e164 && x.Channel == MessageChannel.Sms && x.Status == MessageStatus.Sent
                && x.AppointmentId != null && x.SentAtUtc >= sinceUtc && !x.IsDeleted
                && x.Kind != MessageKind.Cancelled)
            .OrderByDescending(x => x.SentAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<Appointment?> GetAppointmentTrackedAsync(Guid appointmentId, CancellationToken cancellationToken) =>
        dbContext.Appointments.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == appointmentId && !a.IsDeleted, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    // Loads the appointment with its patient, doctor, service and clinic in one query (all unfiltered, by ID).
    private IQueryable<MessageRow> MessageData(IQueryable<Appointment> appointments) =>
        from a in appointments
        join p in dbContext.Patients.IgnoreQueryFilters() on a.PatientId equals p.Id
        join d in dbContext.Doctors.IgnoreQueryFilters() on a.DoctorId equals d.Id
        join s in dbContext.Services.IgnoreQueryFilters() on a.ServiceId equals s.Id
        join c in dbContext.Clinics.IgnoreQueryFilters() on a.ClinicId equals (Guid?)c.Id
        where !p.IsDeleted && !c.IsDeleted
        select new MessageRow(a, p, d, s, c);

    private static AppointmentForMessage ToData(MessageRow r) => new(r.Appointment, r.Patient, r.Doctor, r.Service, r.Clinic);

    private sealed record MessageRow(Appointment Appointment, Patient Patient, Doctor Doctor, Service Service, Clinic Clinic);
}
