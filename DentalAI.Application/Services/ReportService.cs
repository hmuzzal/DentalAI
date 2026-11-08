using DentalAI.Application.Common.Interfaces;
using DentalAI.Application.Common.Interfaces.Persistence;
using DentalAI.Application.Common.Models;
using DentalAI.Application.Common.Reports;
using DentalAI.Application.DTOs.Reports;
using DentalAI.Domain.Enums;

namespace DentalAI.Application.Services;

/// <summary>Practice reports for the signed-in clinic, by its own calendar and in its billing currency.</summary>
public sealed class ReportService(
    IReportRepository reports,
    IBillingRepository billing,
    IClinicRepository clinics,
    ICurrentUserService currentUser,
    IDateTimeProvider clock)
{
    public async Task<Result<PracticeReportDto>> GetPracticeReportAsync(ReportQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.ClinicId is not { } clinicId) return Forbidden<PracticeReportDto>();
        var (zone, currency) = await ClinicContextAsync(clinicId, cancellationToken);
        var today = ReportCalculator.LocalDate(clock.UtcNow, zone);
        var from = query.From ?? new DateOnly(today.Year, today.Month, 1);
        var to = query.To ?? today;

        if (from > to)
            return Result<PracticeReportDto>.Fail(ResultStatus.ValidationError, "The start date must be on or before the end date.");
        if (to.DayNumber - from.DayNumber + 1 > ReportCalculator.MaxDays)
            return Result<PracticeReportDto>.Fail(ResultStatus.ValidationError, $"A report can cover at most {ReportCalculator.MaxDays} days.");

        // One read covering the previous period too, for the comparison.
        var (previousFrom, _) = ReportCalculator.PreviousRange(from, to);
        var (fromUtc, toUtc) = ReportCalculator.UtcWindow(previousFrom, to, zone);

        var appointments = await reports.GetAppointmentsAsync(fromUtc, toUtc, cancellationToken);
        var invoices = await reports.GetIssuedInvoicesAsync(previousFrom, to, cancellationToken);
        var lines = await reports.GetIssuedLinesAsync(from, to, cancellationToken);
        var money = await reports.GetMoneyAsync(fromUtc, toUtc, cancellationToken);
        var added = await reports.GetPatientsAddedAsync(fromUtc, toUtc, cancellationToken);

        var report = ReportCalculator.Build(new ReportInput(from, to, zone, currency, appointments, invoices, lines, money, added));
        // Practice money is for clinic admins; receptionists get the visit and patient numbers.
        return Result<PracticeReportDto>.Ok(currentUser.IsInRole(UserRole.ClinicAdmin) ? report : ReportCalculator.WithoutMoney(report));
    }

    public async Task<Result<ReceivablesReportDto>> GetReceivablesAsync(CancellationToken cancellationToken)
    {
        if (currentUser.ClinicId is not { } clinicId) return Forbidden<ReceivablesReportDto>();
        var (zone, currency) = await ClinicContextAsync(clinicId, cancellationToken);
        var rows = await reports.GetReceivablesAsync(cancellationToken);
        return Result<ReceivablesReportDto>.Ok(ReportCalculator.Receivables(rows, ReportCalculator.LocalDate(clock.UtcNow, zone), currency));
    }

    private async Task<(TimeZoneInfo Zone, string Currency)> ClinicContextAsync(Guid clinicId, CancellationToken cancellationToken)
    {
        var clinic = await clinics.GetByIdAsync(clinicId, cancellationToken);
        var settings = await billing.GetSettingsAsync(cancellationToken);
        return (SchedulingEngine.ResolveTimeZone(clinic?.TimeZone), settings?.Currency ?? "USD");
    }

    private static Result<T> Forbidden<T>() => Result<T>.Fail(ResultStatus.Forbidden, "Clinic context is required.");
}
