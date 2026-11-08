using DentalAI.Domain.Enums;

namespace DentalAI.Application.DTOs.Reports;

public enum ReportGranularity
{
    Day,
    Week,
    Month
}

/// <summary>Clinic-local dates, inclusive. Both default: the current month up to today.</summary>
public sealed class ReportQuery
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
}

/// <param name="Production">Issued invoices (not void) by issue date: subtotal minus discounts, before tax.</param>
/// <param name="Collections">Payments received minus refunds given, by the day they happened.</param>
/// <param name="Booked">Appointments in the range, any status.</param>
/// <param name="NoShowRate">No-shows ÷ (completed + no-shows); null when there were none.</param>
/// <param name="CancellationRate">Cancelled ÷ booked; null when nothing was booked.</param>
/// <param name="PatientsSeen">Different patients with a completed visit.</param>
/// <param name="NewPatients">Patients added to the clinic in the range.</param>
public sealed record ReportKpisDto(
    decimal? Production,
    decimal? Collections,
    int Booked,
    int Completed,
    int Cancelled,
    int NoShows,
    decimal? NoShowRate,
    decimal? CancellationRate,
    int PatientsSeen,
    int NewPatients,
    decimal? ProductionPerVisit);

/// <summary>One bar of the trend. Start and End are clamped to the report range (so the first week can be short).</summary>
public sealed record ReportPeriodDto(DateOnly Start, DateOnly End, decimal? Production, decimal? Collections, int Completed, int NewPatients);

/// <summary>DoctorId is null for invoices and payments with no appointment (so no doctor).</summary>
public sealed record DoctorReportRowDto(
    Guid? DoctorId, string Name, int Booked, int Completed, int Cancelled, int NoShows, decimal? Production, decimal? Collections);

/// <summary>Invoice lines by service (before invoice-level discounts and tax).</summary>
public sealed record ServiceReportRowDto(Guid? ServiceId, string Name, int Quantity, decimal? Amount);

/// <param name="Payments">Number of payments.</param>
/// <param name="Amount">Received minus refunded.</param>
public sealed record PaymentMethodRowDto(PaymentMethod Method, int Payments, decimal Amount);

/// <param name="OtherCurrencyInvoices">Invoices in the range in a currency other than the clinic's: left out of the money figures.</param>
/// <param name="IncludesMoney">False for receptionists: every money figure is null, payment methods are empty and only
/// doctors with appointments are listed. Visit and patient counts are the same for everyone.</param>
public sealed record PracticeReportDto(
    DateOnly From,
    DateOnly To,
    DateOnly PreviousFrom,
    DateOnly PreviousTo,
    string Currency,
    string TimeZone,
    ReportGranularity Granularity,
    ReportKpisDto Current,
    ReportKpisDto Previous,
    IReadOnlyList<ReportPeriodDto> Periods,
    IReadOnlyList<DoctorReportRowDto> Doctors,
    IReadOnlyList<ServiceReportRowDto> Services,
    IReadOnlyList<PaymentMethodRowDto> PaymentMethods,
    int OtherCurrencyInvoices,
    bool IncludesMoney);

/// <summary>Amounts owed, by how many days past the due date (issue date when there is none).</summary>
public sealed record AgingBucketsDto(decimal Current, decimal Days1To30, decimal Days31To60, decimal Days61To90, decimal Over90)
{
    public decimal Total => Current + Days1To30 + Days31To60 + Days61To90 + Over90;
}

public sealed record ReceivablePatientDto(Guid PatientId, string PatientName, int OpenInvoices, DateOnly? OldestDueDate, AgingBucketsDto Buckets);

public sealed record ReceivablesReportDto(
    DateOnly AsOf, string Currency, AgingBucketsDto Totals, IReadOnlyList<ReceivablePatientDto> Patients, int OtherCurrencyInvoices);
