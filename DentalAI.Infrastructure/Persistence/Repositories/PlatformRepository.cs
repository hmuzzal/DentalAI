using DentalAI.Application.Common.Interfaces.Persistence;
using DentalAI.Application.DTOs.Platform;
using DentalAI.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DentalAI.Infrastructure.Persistence.Repositories;

/// <summary>
/// The only place that reads across clinics. It skips the tenant filter deliberately (platform admins have no
/// clinic) and so must filter soft-deleted rows itself, and must only ever return counts.
/// </summary>
public sealed class PlatformRepository(ApplicationDbContext dbContext) : IPlatformRepository
{
    public async Task<PlatformOverviewDto> GetOverviewAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        var since = utcNow.AddDays(-30);

        var clinics = await dbContext.Clinics.AsNoTracking().IgnoreQueryFilters()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, x.Country, x.TimeZone, x.CreatedDate })
            .ToListAsync(cancellationToken);

        var users = await CountByClinicAsync(dbContext.Users.IgnoreQueryFilters()
            .Where(x => !x.IsDeleted && x.IsActive && x.ClinicId != null)
            .Select(x => x.ClinicId), cancellationToken);
        var doctors = await CountByClinicAsync(dbContext.Doctors.IgnoreQueryFilters()
            .Where(x => !x.IsDeleted).Select(x => x.ClinicId), cancellationToken);
        var patients = await CountByClinicAsync(dbContext.Patients.IgnoreQueryFilters()
            .Where(x => !x.IsDeleted).Select(x => x.ClinicId), cancellationToken);
        var recent = await CountByClinicAsync(dbContext.Appointments.IgnoreQueryFilters()
            .Where(x => !x.IsDeleted && x.StartTime >= since && x.StartTime < utcNow).Select(x => x.ClinicId), cancellationToken);
        var upcoming = await CountByClinicAsync(dbContext.Appointments.IgnoreQueryFilters()
            .Where(x => !x.IsDeleted && x.StartTime >= utcNow
                && (x.Status == AppointmentStatus.Pending || x.Status == AppointmentStatus.Confirmed))
            .Select(x => x.ClinicId), cancellationToken);

        var usage = clinics.Select(c => new ClinicUsageDto(
            c.Id, c.Name, c.Country, c.TimeZone, c.CreatedDate,
            users.GetValueOrDefault(c.Id), doctors.GetValueOrDefault(c.Id), patients.GetValueOrDefault(c.Id),
            recent.GetValueOrDefault(c.Id), upcoming.GetValueOrDefault(c.Id))).ToList();

        return new PlatformOverviewDto(
            usage.Count, usage.Sum(x => x.ActiveUsers), usage.Sum(x => x.Doctors), usage.Sum(x => x.Patients),
            usage.Sum(x => x.AppointmentsLast30Days), usage.Sum(x => x.UpcomingAppointments), usage);
    }

    private static async Task<Dictionary<Guid, int>> CountByClinicAsync(IQueryable<Guid?> clinicIds, CancellationToken cancellationToken) =>
        await clinicIds.Where(x => x != null)
            .GroupBy(x => x!.Value)
            .Select(g => new { ClinicId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ClinicId, x => x.Count, cancellationToken);
}
