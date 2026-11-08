using DentalAI.Application.Common.Interfaces.Persistence;
using DentalAI.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DentalAI.Infrastructure.Persistence.Repositories;

/// <summary>Report rows for the signed-in clinic. Every query goes through the tenant and soft-delete filters.</summary>
public sealed class ReportRepository(ApplicationDbContext dbContext) : IReportRepository
{
    public async Task<IReadOnlyList<ReportAppointmentRow>> GetAppointmentsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken) =>
        await dbContext.Appointments.AsNoTracking()
            .Where(a => a.StartTime >= fromUtc && a.StartTime < toUtc)
            .Select(a => new ReportAppointmentRow(
                a.Id,
                DateTime.SpecifyKind(a.StartTime, DateTimeKind.Utc),
                a.Status,
                a.DoctorId,
                a.Doctor != null ? (a.Doctor.FirstName + " " + a.Doctor.LastName).Trim() : "Doctor",
                a.PatientId))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReportInvoiceRow>> GetIssuedInvoicesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await IssuedInvoices(from, to)
            .Select(i => new ReportInvoiceRow(
                i.Id,
                i.IssueDate!.Value,
                i.Currency,
                i.Subtotal,
                i.DiscountAmount,
                i.Appointment != null ? i.Appointment.DoctorId : null,
                i.Appointment != null && i.Appointment.Doctor != null
                    ? (i.Appointment.Doctor.FirstName + " " + i.Appointment.Doctor.LastName).Trim()
                    : null))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReportLineRow>> GetIssuedLinesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var invoiceIds = IssuedInvoices(from, to).Select(i => i.Id);
        var lines = await dbContext.InvoiceLines.AsNoTracking()
            .Where(l => invoiceIds.Contains(l.InvoiceId))
            .Select(l => new
            {
                l.Invoice!.IssueDate,
                l.Invoice.Currency,
                l.ServiceId,
                l.Description,
                l.Quantity,
                l.LineTotal
            })
            .ToListAsync(cancellationToken);

        var serviceIds = lines.Where(l => l.ServiceId.HasValue).Select(l => l.ServiceId!.Value).Distinct().ToList();
        // Deleted services keep their name in reports, so skip the soft-delete filter. The IDs come from this clinic's
        // (tenant-filtered) invoice lines, so they are this clinic's services.
        var names = await dbContext.Services.AsNoTracking().IgnoreQueryFilters()
            .Where(s => serviceIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);

        return lines.Select(l => new ReportLineRow(
                l.IssueDate!.Value,
                l.Currency,
                l.ServiceId,
                l.ServiceId is { } id && names.TryGetValue(id, out var name) ? name : l.Description,
                l.Quantity,
                l.LineTotal))
            .ToList();
    }

    public async Task<IReadOnlyList<ReportMoneyRow>> GetMoneyAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
    {
        var payments = await dbContext.Payments.AsNoTracking()
            .Where(p => p.ReceivedAtUtc >= fromUtc && p.ReceivedAtUtc < toUtc)
            .Select(p => new ReportMoneyRow(
                DateTime.SpecifyKind(p.ReceivedAtUtc, DateTimeKind.Utc),
                p.Amount,
                false,
                p.Method,
                p.Invoice!.Currency,
                p.Invoice.Appointment != null ? p.Invoice.Appointment.DoctorId : null,
                p.Invoice.Appointment != null && p.Invoice.Appointment.Doctor != null
                    ? (p.Invoice.Appointment.Doctor.FirstName + " " + p.Invoice.Appointment.Doctor.LastName).Trim()
                    : null))
            .ToListAsync(cancellationToken);

        var refunds = await dbContext.Refunds.AsNoTracking()
            .Where(r => r.RefundedAtUtc >= fromUtc && r.RefundedAtUtc < toUtc)
            .Select(r => new ReportMoneyRow(
                DateTime.SpecifyKind(r.RefundedAtUtc, DateTimeKind.Utc),
                -r.Amount,
                true,
                r.Payment!.Method,
                r.Payment.Invoice!.Currency,
                r.Payment.Invoice.Appointment != null ? r.Payment.Invoice.Appointment.DoctorId : null,
                r.Payment.Invoice.Appointment != null && r.Payment.Invoice.Appointment.Doctor != null
                    ? (r.Payment.Invoice.Appointment.Doctor.FirstName + " " + r.Payment.Invoice.Appointment.Doctor.LastName).Trim()
                    : null))
            .ToListAsync(cancellationToken);

        return payments.Concat(refunds).ToList();
    }

    public async Task<IReadOnlyList<DateTime>> GetPatientsAddedAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken) =>
        await dbContext.Patients.AsNoTracking()
            .Where(p => p.CreatedDate >= fromUtc && p.CreatedDate < toUtc)
            .Select(p => DateTime.SpecifyKind(p.CreatedDate, DateTimeKind.Utc))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReceivableRow>> GetReceivablesAsync(CancellationToken cancellationToken) =>
        await dbContext.Invoices.AsNoTracking()
            .Where(i => (i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid) && i.Balance > 0)
            .Select(i => new ReceivableRow(
                i.Id,
                i.Number,
                i.PatientId,
                i.Patient != null ? (i.Patient.FirstName + " " + i.Patient.LastName).Trim() : "Patient",
                i.IssueDate,
                i.DueDate,
                i.Balance,
                i.Currency))
            .ToListAsync(cancellationToken);

    private IQueryable<Domain.Entities.Invoice> IssuedInvoices(DateOnly from, DateOnly to) =>
        dbContext.Invoices.AsNoTracking().Where(i =>
            i.Status != InvoiceStatus.Draft && i.Status != InvoiceStatus.Void
            && i.IssueDate != null && i.IssueDate >= from && i.IssueDate <= to);
}
