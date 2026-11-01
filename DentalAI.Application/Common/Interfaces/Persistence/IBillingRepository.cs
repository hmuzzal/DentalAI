using DentalAI.Application.Common.Models;
using DentalAI.Application.DTOs.Billing;
using DentalAI.Domain.Entities;

namespace DentalAI.Application.Common.Interfaces.Persistence;

/// <summary>Invoices, payments and billing settings, always for the signed-in user's clinic (tenant filter).</summary>
public interface IBillingRepository
{
    Task<ClinicBillingSettings?> GetSettingsAsync(CancellationToken cancellationToken);
    void AddSettings(ClinicBillingSettings settings);

    /// <summary>Tracked, with lines, payments and refunds, and the patient.</summary>
    Task<Invoice?> GetInvoiceAsync(Guid id, CancellationToken cancellationToken);
    Task<(IReadOnlyList<Invoice> Items, int TotalCount, IReadOnlyList<CurrencyAmountDto> Outstanding)> ListAsync(
        InvoiceListQuery query, int page, int pageSize, DateTime? createdFromUtc, DateTime? createdToUtc, CancellationToken cancellationToken);
    Task<bool> HasOpenInvoiceForAppointmentAsync(Guid appointmentId, CancellationToken cancellationToken);
    /// <summary>True when another issued invoice already has this number (numbers are unique per clinic).</summary>
    Task<bool> NumberExistsAsync(string number, CancellationToken cancellationToken);

    void AddInvoice(Invoice invoice);
    void AddLine(InvoiceLine line);
    void AddPayment(Payment payment);
    void AddRefund(Refund refund);

    /// <summary>
    /// Money received in a period, for the dashboard: each payment at its received time, each refund as a negative
    /// amount at its refund time, with the clinic's time zone.
    /// </summary>
    Task<IReadOnlyList<DashboardAppointmentRow>> GetRevenueRowsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);
}
