using DentalAI.Application.Common.Interfaces.Persistence;
using DentalAI.Application.DTOs.Reports;
using DentalAI.Domain.Enums;

namespace DentalAI.Application.Common.Reports;

/// <summary>Rows covering the previous period and the report range (PreviousFrom … To), for one clinic.</summary>
public sealed record ReportInput(
    DateOnly From,
    DateOnly To,
    TimeZoneInfo Zone,
    string Currency,
    IReadOnlyList<ReportAppointmentRow> Appointments,
    IReadOnlyList<ReportInvoiceRow> Invoices,
    IReadOnlyList<ReportLineRow> Lines,
    IReadOnlyList<ReportMoneyRow> Money,
    IReadOnlyList<DateTime> PatientsAdded);

/// <summary>
/// Report sums, by clinic-local calendar. Pure functions: the repository supplies rows, this decides which day,
/// period and doctor they belong to. Money figures only count the clinic's currency.
/// </summary>
public static class ReportCalculator
{
    /// <summary>Longest range a report can cover.</summary>
    public const int MaxDays = 366;
    public const string NoDoctor = "No appointment linked";

    /// <summary>Days for up to a month, weeks for up to a quarter, months beyond.</summary>
    public static ReportGranularity GranularityFor(DateOnly from, DateOnly to) => (to.DayNumber - from.DayNumber + 1) switch
    {
        <= 31 => ReportGranularity.Day,
        <= 92 => ReportGranularity.Week,
        _ => ReportGranularity.Month
    };

    /// <summary>The equally long range just before.</summary>
    public static (DateOnly From, DateOnly To) PreviousRange(DateOnly from, DateOnly to)
    {
        var days = to.DayNumber - from.DayNumber + 1;
        return (from.AddDays(-days), from.AddDays(-1));
    }

    /// <summary>UTC instants from local midnight starting <paramref name="from"/> to local midnight after <paramref name="to"/>.</summary>
    public static (DateTime FromUtc, DateTime ToUtc) UtcWindow(DateOnly from, DateOnly to, TimeZoneInfo zone) =>
        (LocalMidnightUtc(from, zone), LocalMidnightUtc(to.AddDays(1), zone));

    public static DateOnly LocalDate(DateTime utc, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone));

    /// <summary>Weeks start on Sunday (US).</summary>
    public static DateOnly PeriodStart(DateOnly date, ReportGranularity granularity) => granularity switch
    {
        ReportGranularity.Week => date.AddDays(-(int)date.DayOfWeek),
        ReportGranularity.Month => new DateOnly(date.Year, date.Month, 1),
        _ => date
    };

    public static PracticeReportDto Build(ReportInput input)
    {
        var (from, to, zone, currency) = (input.From, input.To, input.Zone, input.Currency);
        var (previousFrom, previousTo) = PreviousRange(from, to);
        var granularity = GranularityFor(from, to);

        bool InRange(DateOnly d) => d >= from && d <= to;
        bool InPrevious(DateOnly d) => d >= previousFrom && d <= previousTo;

        var appointments = input.Appointments.Select(a => (Row: a, Date: LocalDate(a.StartUtc, zone))).ToList();
        var invoices = input.Invoices.Where(i => i.Currency == currency).ToList();
        var money = input.Money.Where(m => m.Currency == currency).Select(m => (Row: m, Date: LocalDate(m.AtUtc, zone))).ToList();
        var added = input.PatientsAdded.Select(t => LocalDate(t, zone)).ToList();

        ReportKpisDto Kpis(Func<DateOnly, bool> inPeriod)
        {
            var visits = appointments.Where(a => inPeriod(a.Date)).Select(a => a.Row).ToList();
            var completed = visits.Count(a => a.Status == AppointmentStatus.Completed);
            var noShows = visits.Count(a => a.Status == AppointmentStatus.NoShow);
            var cancelled = visits.Count(a => a.Status == AppointmentStatus.Cancelled);
            var production = invoices.Where(i => inPeriod(i.IssueDate)).Sum(Production);
            return new ReportKpisDto(
                production,
                money.Where(m => inPeriod(m.Date)).Sum(m => m.Row.Amount),
                visits.Count,
                completed,
                cancelled,
                noShows,
                Rate(noShows, completed + noShows),
                Rate(cancelled, visits.Count),
                visits.Where(a => a.Status == AppointmentStatus.Completed).Select(a => a.PatientId).Distinct().Count(),
                added.Count(inPeriod),
                completed > 0 ? Math.Round(production / completed, 2) : null);
        }

        var periods = new List<ReportPeriodDto>();
        for (var start = PeriodStart(from, granularity); start <= to; start = NextPeriod(start, granularity))
        {
            var (s, e) = (start < from ? from : start, Min(NextPeriod(start, granularity).AddDays(-1), to));
            bool InThis(DateOnly d) => d >= s && d <= e;
            periods.Add(new ReportPeriodDto(
                s, e,
                invoices.Where(i => InThis(i.IssueDate)).Sum(Production),
                money.Where(m => InThis(m.Date)).Sum(m => m.Row.Amount),
                appointments.Count(a => InThis(a.Date) && a.Row.Status == AppointmentStatus.Completed),
                added.Count(InThis)));
        }

        return new PracticeReportDto(
            from, to, previousFrom, previousTo, currency, zone.Id, granularity,
            Kpis(InRange), Kpis(InPrevious), periods,
            Doctors(appointments.Where(a => InRange(a.Date)).Select(a => a.Row).ToList(),
                invoices.Where(i => InRange(i.IssueDate)).ToList(),
                money.Where(m => InRange(m.Date)).Select(m => m.Row).ToList()),
            input.Lines.Where(l => l.Currency == currency && InRange(l.IssueDate))
                .GroupBy(l => l.ServiceId?.ToString() ?? l.Name.Trim().ToUpperInvariant())
                .Select(g => new ServiceReportRowDto(g.First().ServiceId, g.First().Name.Trim(), g.Sum(l => l.Quantity), g.Sum(l => l.LineTotal)))
                .OrderByDescending(s => s.Amount).ThenBy(s => s.Name).ToList(),
            money.Where(m => InRange(m.Date)).GroupBy(m => m.Row.Method)
                .Select(g => new PaymentMethodRowDto(g.Key, g.Count(m => !m.Row.IsRefund), g.Sum(m => m.Row.Amount)))
                .OrderByDescending(p => p.Amount).ToList(),
            input.Invoices.Count(i => i.Currency != currency && InRange(i.IssueDate)),
            IncludesMoney: true);
    }

    /// <summary>
    /// The report for staff who don't see practice money (receptionists): counts stay, money figures become null,
    /// payment methods go, and the "no appointment linked" doctor row (which only ever holds money) is dropped.
    /// Services are re-ordered by quantity so their order doesn't give away which earned most.
    /// </summary>
    public static PracticeReportDto WithoutMoney(PracticeReportDto report) => report with
    {
        Current = WithoutMoney(report.Current),
        Previous = WithoutMoney(report.Previous),
        Periods = report.Periods.Select(p => p with { Production = null, Collections = null }).ToList(),
        Doctors = report.Doctors.Where(d => d.DoctorId is not null)
            .Select(d => d with { Production = null, Collections = null })
            .OrderByDescending(d => d.Completed).ThenBy(d => d.Name).ToList(),
        Services = report.Services.Select(s => s with { Amount = null })
            .OrderByDescending(s => s.Quantity).ThenBy(s => s.Name).ToList(),
        PaymentMethods = [],
        OtherCurrencyInvoices = 0,
        IncludesMoney = false
    };

    private static ReportKpisDto WithoutMoney(ReportKpisDto kpis) =>
        kpis with { Production = null, Collections = null, ProductionPerVisit = null };

    public static ReceivablesReportDto Receivables(IReadOnlyList<ReceivableRow> rows, DateOnly asOf, string currency)
    {
        var mine = rows.Where(r => r.Currency == currency && r.Balance > 0).ToList();
        var patients = mine.GroupBy(r => r.PatientId)
            .Select(g => new ReceivablePatientDto(
                g.Key,
                g.First().PatientName,
                g.Count(),
                g.Min(r => r.DueDate ?? r.IssueDate),
                Buckets(g, asOf)))
            .OrderByDescending(p => p.Buckets.Over90 + p.Buckets.Days61To90)
            .ThenByDescending(p => p.Buckets.Total)
            .ThenBy(p => p.PatientName)
            .ToList();
        return new ReceivablesReportDto(asOf, currency, Buckets(mine, asOf), patients, rows.Count(r => r.Currency != currency && r.Balance > 0));
    }

    /// <summary>Days past due on <paramref name="asOf"/>; 0 or less means not yet due.</summary>
    public static int DaysPastDue(ReceivableRow row, DateOnly asOf) =>
        (row.DueDate ?? row.IssueDate) is { } due ? asOf.DayNumber - due.DayNumber : 0;

    private static AgingBucketsDto Buckets(IEnumerable<ReceivableRow> rows, DateOnly asOf)
    {
        decimal current = 0, d30 = 0, d60 = 0, d90 = 0, over = 0;
        foreach (var row in rows)
        {
            switch (DaysPastDue(row, asOf))
            {
                case <= 0: current += row.Balance; break;
                case <= 30: d30 += row.Balance; break;
                case <= 60: d60 += row.Balance; break;
                case <= 90: d90 += row.Balance; break;
                default: over += row.Balance; break;
            }
        }
        return new AgingBucketsDto(current, d30, d60, d90, over);
    }

    private static List<DoctorReportRowDto> Doctors(
        List<ReportAppointmentRow> appointments, List<ReportInvoiceRow> invoices, List<ReportMoneyRow> money)
    {
        var names = new Dictionary<Guid, string>();
        foreach (var a in appointments) names[a.DoctorId] = a.DoctorName;
        foreach (var i in invoices) if (i.DoctorId is { } id) names.TryAdd(id, i.DoctorName ?? "Doctor");
        foreach (var m in money) if (m.DoctorId is { } id) names.TryAdd(id, m.DoctorName ?? "Doctor");

        var keys = names.Keys.Select(k => (Guid?)k).ToList();
        if (invoices.Any(i => i.DoctorId is null) || money.Any(m => m.DoctorId is null)) keys.Add(null);

        return keys.Select(id =>
            {
                var visits = appointments.Where(a => a.DoctorId == id).ToList();
                return new DoctorReportRowDto(
                    id,
                    id is { } d ? names[d] : NoDoctor,
                    visits.Count,
                    visits.Count(a => a.Status == AppointmentStatus.Completed),
                    visits.Count(a => a.Status == AppointmentStatus.Cancelled),
                    visits.Count(a => a.Status == AppointmentStatus.NoShow),
                    invoices.Where(i => i.DoctorId == id).Sum(Production),
                    money.Where(m => m.DoctorId == id).Sum(m => m.Amount));
            })
            .OrderBy(r => r.DoctorId is null).ThenByDescending(r => r.Production).ThenBy(r => r.Name)
            .ToList();
    }

    private static decimal Production(ReportInvoiceRow invoice) => invoice.Subtotal - invoice.Discount;

    private static decimal? Rate(int part, int whole) => whole == 0 ? null : Math.Round((decimal)part / whole, 4);

    private static DateOnly NextPeriod(DateOnly start, ReportGranularity granularity) => granularity switch
    {
        ReportGranularity.Week => start.AddDays(7),
        ReportGranularity.Month => start.AddMonths(1),
        _ => start.AddDays(1)
    };

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;

    /// <summary>Where a day begins in the zone. If midnight doesn't exist (a DST jump at 00:00), the first valid minute.</summary>
    private static DateTime LocalMidnightUtc(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(30);
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }
}
