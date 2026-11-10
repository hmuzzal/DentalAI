using DentalAI.Application.Common.Interfaces.Persistence;
using DentalAI.Domain.Entities;
using DentalAI.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DentalAI.Infrastructure.Persistence.Repositories;

/// <summary>
/// The anonymous methods skip the tenant filter on purpose (nobody is signed in yet) and therefore filter
/// soft-deleted rows and scope by clinic ID themselves.
/// </summary>
public sealed class PortalRepository(ApplicationDbContext dbContext) : IPortalRepository
{
    // ---- Anonymous (explicit clinic) ----

    public async Task<(ClinicPortalSettings Settings, Clinic Clinic)?> GetBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        var row = await (
                from settings in dbContext.ClinicPortalSettings.IgnoreQueryFilters()
                join clinic in dbContext.Clinics.IgnoreQueryFilters() on settings.ClinicId equals clinic.Id
                where settings.Slug == slug && !settings.IsDeleted && !clinic.IsDeleted
                select new { settings, clinic })
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : (row.settings, row.clinic);
    }

    public Task<bool> SlugTakenAsync(string slug, Guid exceptClinicId, CancellationToken cancellationToken) =>
        dbContext.ClinicPortalSettings.IgnoreQueryFilters()
            .AnyAsync(x => x.Slug == slug && x.ClinicId != exceptClinicId, cancellationToken);

    public async Task<IReadOnlyList<Patient>> FindPatientsByContactAsync(
        Guid clinicId, string contact, MessageChannel channel, CancellationToken cancellationToken)
    {
        var query = dbContext.Patients.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.ClinicId == clinicId && !p.IsDeleted);
        query = channel == MessageChannel.Email
            ? query.Where(p => p.Email == contact)
            : query.Where(p => p.Phone == contact);
        return await query.OrderBy(p => p.FirstName).ThenBy(p => p.LastName).Take(20).ToListAsync(cancellationToken);
    }

    public Task<int> CountCodesSinceAsync(Guid clinicId, string contact, DateTime sinceUtc, CancellationToken cancellationToken) =>
        dbContext.PortalSignInCodes.IgnoreQueryFilters()
            .CountAsync(x => x.ClinicId == clinicId && x.Contact == contact && x.CreatedDate >= sinceUtc, cancellationToken);

    public Task<PortalSignInCode?> GetOpenCodeAsync(
        Guid clinicId, string contact, DateTime nowUtc, int maxAttempts, CancellationToken cancellationToken) =>
        dbContext.PortalSignInCodes.IgnoreQueryFilters()
            .Where(x => x.ClinicId == clinicId && x.Contact == contact && !x.IsDeleted
                && x.VerifiedAtUtc == null && x.ExpiresAtUtc > nowUtc && x.FailedAttempts < maxAttempts)
            .OrderByDescending(x => x.CreatedDate)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<PortalSignInCode?> GetByTicketHashAsync(Guid clinicId, string ticketHash, CancellationToken cancellationToken) =>
        dbContext.PortalSignInCodes.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.ClinicId == clinicId && x.TicketHash == ticketHash && !x.IsDeleted, cancellationToken);

    public Task<PatientPortalSession?> GetSessionAsync(string tokenHash, CancellationToken cancellationToken) =>
        dbContext.PatientPortalSessions.IgnoreQueryFilters()
            .Include(x => x.Patient)
            .FirstOrDefaultAsync(x => x.TokenHash == tokenHash && !x.IsDeleted, cancellationToken);

    public Task<ClinicPortalSettings?> GetSettingsForClinicAsync(Guid clinicId, CancellationToken cancellationToken) =>
        dbContext.ClinicPortalSettings.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.ClinicId == clinicId && !x.IsDeleted, cancellationToken);

    public Task<Clinic?> GetClinicAsync(Guid clinicId, CancellationToken cancellationToken) =>
        dbContext.Clinics.AsNoTracking().FirstOrDefaultAsync(x => x.Id == clinicId, cancellationToken);

    public void AddCode(PortalSignInCode code) => dbContext.PortalSignInCodes.Add(code);
    public void AddSession(PatientPortalSession session) => dbContext.PatientPortalSessions.Add(session);
    public void AddPatient(Patient patient) => dbContext.Patients.Add(patient);
    public void AddSettings(ClinicPortalSettings settings) => dbContext.ClinicPortalSettings.Add(settings);

    // ---- Signed-in clinic (tenant filter) ----

    public Task<ClinicPortalSettings?> GetSettingsAsync(CancellationToken cancellationToken) =>
        dbContext.ClinicPortalSettings.FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Appointment>> ListAppointmentsAsync(Guid patientId, int take, CancellationToken cancellationToken) =>
        await dbContext.Appointments.AsNoTracking()
            .Include(a => a.Doctor)
            .Include(a => a.Service)
            .Where(a => a.PatientId == patientId)
            .OrderByDescending(a => a.StartTime)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<int> CountUpcomingAsync(Guid patientId, DateTime nowUtc, CancellationToken cancellationToken) =>
        dbContext.Appointments.CountAsync(a => a.PatientId == patientId && a.StartTime > nowUtc
            && (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed), cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
