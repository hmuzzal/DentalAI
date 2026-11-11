using DentalAI.Application.Common.Phone;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DentalAI.Infrastructure.Persistence;

/// <summary>
/// Rewrites patient phone numbers saved before numbers were stored in E.164 (e.g. "(212) 555-0142") into E.164,
/// read in the clinic's country. The portal finds patients by E.164 phone and SMS providers need it, so an
/// old-format number means no sign-in by phone and no texts. Runs at startup; once every number is converted
/// the query matches nothing. Numbers that can't be read are left as they are (staff see "Check phone").
/// </summary>
public static class PatientPhoneBackfill
{
    private const int BatchSize = 500;

    public static async Task<(int Converted, int Unreadable)> RunAsync(
        ApplicationDbContext db, ILogger logger, CancellationToken cancellationToken)
    {
        var converted = 0;
        var unreadable = new List<Guid>();

        while (true)
        {
            // Anything not "+digits": no leading +, or any character other than + and digits.
            // Converted rows drop out of the query; unreadable ones stay, ahead of the rest in Id order, so skip them.
            var batch = await db.Patients
                .IgnoreQueryFilters()
                .Where(p => p.Phone != ""
                            && (!p.Phone.StartsWith("+") || EF.Functions.Like(p.Phone, "%[^+0-9]%")))
                .OrderBy(p => p.Id)
                .Skip(unreadable.Count)
                .Take(BatchSize)
                .Select(p => new { Patient = p, Country = p.Clinic != null ? p.Clinic.Country : null })
                .ToListAsync(cancellationToken);
            if (batch.Count == 0) break;

            foreach (var row in batch)
            {
                if (PhoneNumberRules.TryNormalize(row.Patient.Phone, row.Country, out var e164))
                {
                    row.Patient.Phone = e164;
                    converted++;
                }
                else
                {
                    unreadable.Add(row.Patient.Id);
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        if (converted > 0)
        {
            logger.LogInformation("Converted {Count} patient phone number(s) to E.164.", converted);
        }

        if (unreadable.Count > 0)
        {
            logger.LogWarning(
                "{Count} patient phone number(s) could not be read and need checking by the clinic: {PatientIds}",
                unreadable.Count, unreadable);
        }

        return (converted, unreadable.Count);
    }
}
