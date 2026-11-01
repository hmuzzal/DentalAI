using DentalAI.Application.Common.Interfaces.Persistence;
using DentalAI.Application.Common.Models;
using DentalAI.Application.DTOs.Billing;
using DentalAI.Domain.Entities;
using DentalAI.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DentalAI.Infrastructure.Persistence.Repositories;

/// <summary>All queries go through the tenant filter, so they only ever see the signed-in user's clinic.</summary>
public sealed class BillingRepository(ApplicationDbContext dbContext) : IBillingRepository
{
    public Task<ClinicBillingSettings?> GetSettingsAsync(CancellationToken cancellationToken) =>
        dbContext.ClinicBillingSettings.FirstOrDefaultAsync(cancellationToken);

    public void AddSettings(ClinicBillingSettings settings) => dbContext.ClinicBillingSettings.Add(settings);

    public Task<Invoice?> GetInvoiceAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Invoices
            .Include(x => x.Patient)
            .Include(x => x.Lines)
            .Include(x => x.Payments).ThenInclude(p => p.Refunds)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<Invoice> Items, int TotalCount, IReadOnlyList<CurrencyAmountDto> Outstanding)> ListAsync(
        InvoiceListQuery query, int page, int pageSize, DateTime? createdFromUtc, DateTime? createdToUtc, CancellationToken cancellationToken)
    {
        var invoices = dbContext.Invoices.AsNoTracking().Include(x => x.Patient).AsQueryable();

        if (query.Status.HasValue) invoices = invoices.Where(x => x.Status == query.Status.Value);
        if (query.OutstandingOnly)
            invoices = invoices.Where(x => x.Status == InvoiceStatus.Issued || x.Status == InvoiceStatus.PartiallyPaid);
        if (query.PatientId.HasValue) invoices = invoices.Where(x => x.PatientId == query.PatientId.Value);
        if (query.AppointmentId.HasValue) invoices = invoices.Where(x => x.AppointmentId == query.AppointmentId.Value);

        // Issued invoices filter on their issue date; drafts (no issue date) on the day they were created.
        if (query.From.HasValue)
        {
            var from = query.From.Value;
            var createdFrom = createdFromUtc!.Value;
            invoices = invoices.Where(x => x.IssueDate != null ? x.IssueDate >= from : x.CreatedDate >= createdFrom);
        }
        if (query.To.HasValue)
        {
            var to = query.To.Value;
            var createdTo = createdToUtc!.Value;
            invoices = invoices.Where(x => x.IssueDate != null ? x.IssueDate <= to : x.CreatedDate < createdTo);
        }

        foreach (var word in (query.Search ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(5))
        {
            invoices = invoices.Where(x => (x.Number != null && x.Number.Contains(word))
                || x.Patient!.FirstName.Contains(word) || x.Patient.LastName.Contains(word));
        }

        var total = await invoices.CountAsync(cancellationToken);
        var outstanding = await invoices
            .Where(x => x.Status == InvoiceStatus.Issued || x.Status == InvoiceStatus.PartiallyPaid)
            .GroupBy(x => x.Currency)
            .Select(g => new CurrencyAmountDto(g.Key, g.Sum(x => x.Balance)))
            .ToListAsync(cancellationToken);
        var items = await invoices
            .OrderByDescending(x => x.IssueDate ?? DateOnly.MaxValue) // drafts first, then newest issued
            .ThenByDescending(x => x.CreatedDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total, outstanding);
    }

    public Task<bool> HasOpenInvoiceForAppointmentAsync(Guid appointmentId, CancellationToken cancellationToken) =>
        dbContext.Invoices.AnyAsync(x => x.AppointmentId == appointmentId && x.Status != InvoiceStatus.Void, cancellationToken);

    public Task<bool> NumberExistsAsync(string number, CancellationToken cancellationToken) =>
        dbContext.Invoices.AnyAsync(x => x.Number == number, cancellationToken);

    public void AddInvoice(Invoice invoice) => dbContext.Invoices.Add(invoice);
    public void AddLine(InvoiceLine line) => dbContext.InvoiceLines.Add(line);
    public void AddPayment(Payment payment) => dbContext.Payments.Add(payment);
    public void AddRefund(Refund refund) => dbContext.Refunds.Add(refund);

    public async Task<IReadOnlyList<DashboardAppointmentRow>> GetRevenueRowsAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
    {
        var payments = await dbContext.Payments.AsNoTracking()
            .Where(p => p.ReceivedAtUtc >= fromUtc && p.ReceivedAtUtc < toUtc)
            .Select(p => new { At = p.ReceivedAtUtc, p.Amount, p.ClinicId })
            .ToListAsync(cancellationToken);
        var refunds = await dbContext.Refunds.AsNoTracking()
            .Where(r => r.RefundedAtUtc >= fromUtc && r.RefundedAtUtc < toUtc)
            .Select(r => new { At = r.RefundedAtUtc, Amount = -r.Amount, r.ClinicId })
            .ToListAsync(cancellationToken);

        var clinicIds = payments.Select(x => x.ClinicId).Concat(refunds.Select(x => x.ClinicId)).Distinct().ToList();
        var zones = await dbContext.Clinics.AsNoTracking()
            .Where(c => clinicIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.TimeZone, cancellationToken);

        return payments.Concat(refunds)
            .Select(x => new DashboardAppointmentRow(
                DateTime.SpecifyKind(x.At, DateTimeKind.Utc), AppointmentStatus.Completed, x.Amount,
                x.ClinicId.HasValue ? zones.GetValueOrDefault(x.ClinicId.Value) : null))
            .ToList();
    }
}
