using DentalAI.Domain.Enums;

namespace DentalAI.Application.Common.Interfaces.Persistence;

/// <summary>An appointment, for the practice report.</summary>
public sealed record ReportAppointmentRow(Guid Id, DateTime StartUtc, AppointmentStatus Status, Guid DoctorId, string DoctorName, Guid PatientId);

/// <summary>An issued (not draft, not void) invoice. Production is <c>Subtotal - Discount</c> (tax isn't production).</summary>
public sealed record ReportInvoiceRow(Guid Id, DateOnly IssueDate, string Currency, decimal Subtotal, decimal Discount, Guid? DoctorId, string? DoctorName);

/// <summary>A line of an issued invoice. <c>Name</c> is the service's name when the line has one, otherwise its description.</summary>
public sealed record ReportLineRow(DateOnly IssueDate, string Currency, Guid? ServiceId, string Name, int Quantity, decimal LineTotal);

/// <summary>Money received: a payment (positive) or a refund (negative), at the time it happened.</summary>
public sealed record ReportMoneyRow(DateTime AtUtc, decimal Amount, bool IsRefund, PaymentMethod Method, string Currency, Guid? DoctorId, string? DoctorName);

/// <summary>An invoice with something still owed.</summary>
public sealed record ReceivableRow(Guid InvoiceId, string? Number, Guid PatientId, string PatientName, DateOnly? IssueDate, DateOnly? DueDate, decimal Balance, string Currency);

/// <summary>Raw rows for reports, all for the signed-in clinic (tenant filter). The report calculator does the sums.</summary>
public interface IReportRepository
{
    Task<IReadOnlyList<ReportAppointmentRow>> GetAppointmentsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReportInvoiceRow>> GetIssuedInvoicesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReportLineRow>> GetIssuedLinesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReportMoneyRow>> GetMoneyAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);
    /// <summary>When each patient registered in the window was added.</summary>
    Task<IReadOnlyList<DateTime>> GetPatientsAddedAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReceivableRow>> GetReceivablesAsync(CancellationToken cancellationToken);
}
