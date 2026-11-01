using DentalAI.Application.Common.Billing;
using DentalAI.Application.Common.Interfaces;
using DentalAI.Application.Common.Interfaces.Persistence;
using DentalAI.Application.Common.Models;
using DentalAI.Application.DTOs.Billing;
using DentalAI.Domain.Entities;
using DentalAI.Domain.Enums;

namespace DentalAI.Application.Services;

/// <summary>
/// Invoices and payments for the signed-in user's clinic. No payment provider: payments are recorded as received
/// (cash, card terminal, check, insurance). Rules:
/// - Drafts are editable and unnumbered; issuing gives the next number (under a per-clinic lock) and fixes dates.
/// - Issued invoices change only by payment, refund or void. Payments can't exceed the balance; refunds can't exceed
///   what's left of a payment; only invoices with nothing paid can be voided.
/// - Every change runs under a per-invoice lock and reloads the invoice inside it, so concurrent payments can't overpay.
/// </summary>
public sealed class BillingService(
    IBillingRepository billing,
    IPatientRepository patients,
    IAppointmentRepository appointments,
    IServiceRepository services,
    IClinicRepository clinics,
    ICurrentUserService currentUser,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork)
{
    // ---- Settings ----

    public async Task<Result<BillingSettingsDto>> GetSettingsAsync(CancellationToken cancellationToken) =>
        await WithClinicAsync(async clinicId =>
            Result<BillingSettingsDto>.Ok(ToDto(await GetOrCreateSettingsAsync(clinicId, cancellationToken))));

    public async Task<Result<BillingSettingsDto>> UpdateSettingsAsync(UpdateBillingSettingsRequest request, CancellationToken cancellationToken) =>
        await WithClinicAsync(clinicId => unitOfWork.RunWithLockAsync($"billing-settings:{clinicId:N}", async ct =>
        {
            var settings = await GetOrCreateSettingsAsync(clinicId, ct);
            settings.Currency = request.Currency.Trim().ToUpperInvariant();
            settings.InvoicePrefix = request.InvoicePrefix.Trim().ToUpperInvariant();
            settings.PaymentTermsDays = request.PaymentTermsDays;
            settings.DefaultTaxRatePercent = request.DefaultTaxRatePercent;
            settings.ToothNumbering = request.ToothNumbering;
            await unitOfWork.SaveChangesAsync(ct);
            return Result<BillingSettingsDto>.Ok(ToDto(settings), "Billing settings saved. New invoices use them; existing invoices keep theirs.");
        }, cancellationToken));

    // ---- Invoices ----

    public async Task<Result<InvoiceListDto>> ListAsync(InvoiceListQuery query, CancellationToken cancellationToken) =>
        await WithClinicAsync(async clinicId =>
        {
            var page = Math.Max(1, query.Page);
            var pageSize = Math.Clamp(query.PageSize, 1, InvoiceListQuery.MaxPageSize);
            var zone = await ClinicZoneAsync(clinicId, cancellationToken);
            DateTime? createdFrom = query.From.HasValue ? SchedulingEngine.ToUtc(query.From.Value, TimeOnly.MinValue, zone) : null;
            DateTime? createdTo = query.To.HasValue ? SchedulingEngine.ToUtc(query.To.Value.AddDays(1), TimeOnly.MinValue, zone) : null;

            var (items, total, outstanding) = await billing.ListAsync(query, page, pageSize, createdFrom, createdTo, cancellationToken);
            var today = Today(zone);
            return Result<InvoiceListDto>.Ok(new InvoiceListDto(
                items.Select(x => ToSummary(x, today)).ToList(), total, page, pageSize, outstanding));
        });

    public async Task<Result<InvoiceDto>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await WithClinicAsync(async clinicId =>
        {
            var invoice = await billing.GetInvoiceAsync(id, cancellationToken);
            return invoice is null
                ? NotFound()
                : Result<InvoiceDto>.Ok(ToDto(invoice, Today(await ClinicZoneAsync(clinicId, cancellationToken))));
        });

    /// <summary>The invoice with the clinic's and patient's names and addresses, for printing.</summary>
    public async Task<Result<InvoiceDocumentDto>> GetDocumentAsync(Guid id, CancellationToken cancellationToken) =>
        await WithClinicAsync(async clinicId =>
        {
            var invoice = await billing.GetInvoiceAsync(id, cancellationToken);
            if (invoice is null) return Result<InvoiceDocumentDto>.Fail(ResultStatus.NotFound, "Invoice was not found.");

            var clinic = await clinics.GetByIdAsync(clinicId, cancellationToken);
            var settings = await GetOrCreateSettingsAsync(clinicId, cancellationToken);
            var patient = invoice.Patient!;
            var cityLine = string.Join(", ", new[] { patient.City, string.Join(" ", new[] { patient.State, patient.PostalCode }
                .Where(x => !string.IsNullOrWhiteSpace(x))) }.Where(x => !string.IsNullOrWhiteSpace(x)));

            return Result<InvoiceDocumentDto>.Ok(new InvoiceDocumentDto(
                ToDto(invoice, Today(SchedulingEngine.ResolveTimeZone(clinic?.TimeZone))),
                new InvoicePartyDto(clinic?.Name ?? string.Empty, Optional(clinic?.Address), null, null, Optional(clinic?.Phone), Optional(clinic?.Email)),
                new InvoicePartyDto($"{patient.FirstName} {patient.LastName}".Trim(), patient.AddressLine1, patient.AddressLine2,
                    Optional(cityLine), patient.Phone, patient.Email),
                settings.ToothNumbering));
        });

    public async Task<Result<InvoiceDto>> CreateAsync(CreateInvoiceRequest request, CancellationToken cancellationToken) =>
        await WithClinicAsync(async clinicId =>
        {
            var patient = await patients.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient is null) return Result<InvoiceDto>.Fail(ResultStatus.NotFound, "Patient was not found.");

            if (request.AppointmentId is { } appointmentId)
            {
                var problem = await CheckAppointmentAsync(appointmentId, patient.Id, cancellationToken);
                if (problem is not null) return problem;
            }

            var lineProblem = await CheckServicesAsync(request.Lines, cancellationToken);
            if (lineProblem is not null) return lineProblem;

            var settings = await GetOrCreateSettingsAsync(clinicId, cancellationToken);
            var invoice = new Invoice
            {
                ClinicId = clinicId,
                PatientId = patient.Id,
                Patient = patient,
                AppointmentId = request.AppointmentId,
                Currency = settings.Currency,
                DiscountAmount = request.DiscountAmount,
                TaxRatePercent = request.TaxRatePercent ?? settings.DefaultTaxRatePercent,
                Notes = Optional(request.Notes)
            };
            SetLines(invoice, request.Lines, request.InsuranceCoveragePercent);
            InvoiceCalculator.Recalculate(invoice);
            billing.AddInvoice(invoice);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<InvoiceDto>.Ok(ToDto(invoice, Today(await ClinicZoneAsync(clinicId, cancellationToken))), "Draft invoice created.");
        });

    /// <summary>A draft with one line for the appointment's service at its current price.</summary>
    public async Task<Result<InvoiceDto>> CreateFromAppointmentAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        var appointment = await appointments.GetByIdAsync(appointmentId, cancellationToken);
        if (appointment is null) return Result<InvoiceDto>.Fail(ResultStatus.NotFound, "Appointment was not found.");
        var service = appointment.Service;
        if (service is null) return Result<InvoiceDto>.Fail(ResultStatus.ValidationError, "The appointment has no service to bill.");

        return await CreateAsync(new CreateInvoiceRequest
        {
            PatientId = appointment.PatientId,
            AppointmentId = appointment.Id,
            Lines = [new InvoiceLineRequest { ServiceId = service.Id, Description = service.Name, Quantity = 1, UnitPrice = service.Price }]
        }, cancellationToken);
    }

    public async Task<Result<InvoiceDto>> UpdateDraftAsync(Guid id, UpdateInvoiceRequest request, CancellationToken cancellationToken) =>
        await InInvoiceLockAsync(id, async (invoice, today, ct) =>
        {
            if (invoice.Status != InvoiceStatus.Draft)
                return Result<InvoiceDto>.Fail(ResultStatus.Conflict, "Only drafts can be edited. Issued invoices change through payments, refunds or voiding.");

            var lineProblem = await CheckServicesAsync(request.Lines, ct);
            if (lineProblem is not null) return lineProblem;

            foreach (var line in invoice.Lines.Where(x => !x.IsDeleted)) line.IsDeleted = true;
            SetLines(invoice, request.Lines, request.InsuranceCoveragePercent);
            invoice.DiscountAmount = request.DiscountAmount;
            invoice.TaxRatePercent = request.TaxRatePercent;
            invoice.Notes = Optional(request.Notes);
            InvoiceCalculator.Recalculate(invoice);
            await unitOfWork.SaveChangesAsync(ct);
            return Result<InvoiceDto>.Ok(ToDto(invoice, today), "Draft saved.");
        }, cancellationToken);

    public async Task<Result> DeleteDraftAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await InInvoiceLockAsync(id, async (invoice, today, ct) =>
        {
            if (invoice.Status != InvoiceStatus.Draft)
                return Result<InvoiceDto>.Fail(ResultStatus.Conflict, "Only drafts can be deleted. Void an issued invoice instead.");
            // Soft delete, invoice and lines together (Remove() would try to detach the lines first).
            foreach (var line in invoice.Lines) line.IsDeleted = true;
            invoice.IsDeleted = true;
            await unitOfWork.SaveChangesAsync(ct);
            return Result<InvoiceDto>.Ok(ToDto(invoice, today));
        }, cancellationToken);
        return result.Success ? Result.Ok("Draft deleted.") : Result.Fail(result.Status, result.Message, result.Errors);
    }

    /// <summary>Gives the draft the clinic's next number, today's date and a due date from the payment terms.</summary>
    public async Task<Result<InvoiceDto>> IssueAsync(Guid id, CancellationToken cancellationToken) =>
        await WithClinicAsync(clinicId =>
            // Numbering lock first, then the invoice lock (always this order, so issuing can't deadlock).
            unitOfWork.RunWithLockAsync($"invoice-number:{clinicId:N}", ct =>
                InInvoiceLockAsync(id, async (invoice, today, innerCt) =>
                {
                    if (invoice.Status != InvoiceStatus.Draft)
                        return Result<InvoiceDto>.Fail(ResultStatus.Conflict, $"Invoice {invoice.Number} is already issued.");
                    if (!invoice.Lines.Any(x => !x.IsDeleted))
                        return Result<InvoiceDto>.Fail(ResultStatus.ValidationError, "Add at least one line before issuing.");

                    var settings = await GetOrCreateSettingsAsync(clinicId, innerCt);
                    string number;
                    do
                    {
                        number = $"{settings.InvoicePrefix}-{settings.NextInvoiceNumber:D6}";
                        settings.NextInvoiceNumber++;
                    }
                    while (await billing.NumberExistsAsync(number, innerCt)); // e.g. after the prefix was changed back

                    invoice.Number = number;
                    invoice.Status = InvoiceStatus.Issued;
                    invoice.IssueDate = today;
                    invoice.DueDate = today.AddDays(settings.PaymentTermsDays);
                    InvoiceCalculator.Recalculate(invoice);
                    await unitOfWork.SaveChangesAsync(innerCt);
                    return Result<InvoiceDto>.Ok(ToDto(invoice, today), $"Invoice {number} issued.");
                }, ct), cancellationToken));

    public async Task<Result<InvoiceDto>> RecordPaymentAsync(Guid id, RecordPaymentRequest request, CancellationToken cancellationToken) =>
        await InInvoiceLockAsync(id, async (invoice, today, ct) =>
        {
            if (invoice.Status is not (InvoiceStatus.Issued or InvoiceStatus.PartiallyPaid))
                return Result<InvoiceDto>.Fail(ResultStatus.Conflict, invoice.Status switch
                {
                    InvoiceStatus.Draft => "Issue the invoice before recording payments.",
                    InvoiceStatus.Paid => "This invoice is already paid in full.",
                    _ => "This invoice is void."
                });

            var amount = CurrencyRules.Round(request.Amount, invoice.Currency);
            if (amount <= 0)
                return Result<InvoiceDto>.Fail(ResultStatus.ValidationError, "The amount must be more than 0.");
            if (amount > invoice.Balance)
                return Result<InvoiceDto>.Fail(ResultStatus.ValidationError,
                    $"The payment ({amount:0.##}) is more than the balance ({invoice.Balance:0.##}).");

            var now = clock.UtcNow;
            var receivedAt = request.ReceivedAtUtc.HasValue ? DateTime.SpecifyKind(request.ReceivedAtUtc.Value, DateTimeKind.Utc) : now;
            if (receivedAt > now.AddMinutes(5))
                return Result<InvoiceDto>.Fail(ResultStatus.ValidationError, "The payment date can't be in the future.");

            var payment = new Payment
            {
                ClinicId = invoice.ClinicId,
                InvoiceId = invoice.Id,
                Amount = amount,
                Method = request.Method,
                Reference = Optional(request.Reference),
                ReceivedAtUtc = receivedAt,
                Note = Optional(request.Note)
            };
            invoice.Payments.Add(payment);
            billing.AddPayment(payment);
            InvoiceCalculator.Recalculate(invoice);
            await unitOfWork.SaveChangesAsync(ct);
            return Result<InvoiceDto>.Ok(ToDto(invoice, today), "Payment recorded.");
        }, cancellationToken);

    /// <summary>Clinic admins only (checked by the controller).</summary>
    public async Task<Result<InvoiceDto>> RefundAsync(Guid id, Guid paymentId, RefundRequest request, CancellationToken cancellationToken) =>
        await InInvoiceLockAsync(id, async (invoice, today, ct) =>
        {
            var payment = invoice.Payments.FirstOrDefault(p => p.Id == paymentId && !p.IsDeleted);
            if (payment is null) return Result<InvoiceDto>.Fail(ResultStatus.NotFound, "Payment was not found on this invoice.");

            var amount = CurrencyRules.Round(request.Amount, invoice.Currency);
            var refundable = payment.Amount - payment.RefundedAmount;
            if (amount <= 0 || amount > refundable)
                return Result<InvoiceDto>.Fail(ResultStatus.ValidationError,
                    $"You can refund up to {refundable:0.##} of this payment.");

            var refund = new Refund
            {
                ClinicId = invoice.ClinicId,
                PaymentId = payment.Id,
                Amount = amount,
                Reason = request.Reason.Trim(),
                RefundedAtUtc = clock.UtcNow
            };
            payment.Refunds.Add(refund);
            billing.AddRefund(refund);
            InvoiceCalculator.Recalculate(invoice);
            await unitOfWork.SaveChangesAsync(ct);
            return Result<InvoiceDto>.Ok(ToDto(invoice, today), "Refund recorded.");
        }, cancellationToken);

    /// <summary>Clinic admins only (checked by the controller). Only invoices with nothing paid (refund first).</summary>
    public async Task<Result<InvoiceDto>> VoidAsync(Guid id, VoidInvoiceRequest request, CancellationToken cancellationToken) =>
        await InInvoiceLockAsync(id, async (invoice, today, ct) =>
        {
            if (invoice.Status == InvoiceStatus.Draft)
                return Result<InvoiceDto>.Fail(ResultStatus.Conflict, "Drafts aren't voided; delete the draft instead.");
            if (invoice.Status == InvoiceStatus.Void)
                return Result<InvoiceDto>.Fail(ResultStatus.Conflict, "This invoice is already void.");
            if (invoice.AmountPaid > 0)
                return Result<InvoiceDto>.Fail(ResultStatus.Conflict, "Refund the payments on this invoice before voiding it.");

            invoice.Status = InvoiceStatus.Void;
            invoice.VoidReason = request.Reason.Trim();
            invoice.VoidedAtUtc = clock.UtcNow;
            InvoiceCalculator.Recalculate(invoice);
            await unitOfWork.SaveChangesAsync(ct);
            return Result<InvoiceDto>.Ok(ToDto(invoice, today), $"Invoice {invoice.Number} voided.");
        }, cancellationToken);

    // ---- Helpers ----

    /// <summary>Runs <paramref name="work"/> under the invoice's lock with the invoice loaded inside it.</summary>
    private Task<Result<InvoiceDto>> InInvoiceLockAsync(
        Guid id, Func<Invoice, DateOnly, CancellationToken, Task<Result<InvoiceDto>>> work, CancellationToken cancellationToken) =>
        WithClinicAsync(clinicId => unitOfWork.RunWithLockAsync($"invoice:{id:N}", async ct =>
        {
            var invoice = await billing.GetInvoiceAsync(id, ct);
            if (invoice is null) return NotFound();
            return await work(invoice, Today(await ClinicZoneAsync(clinicId, ct)), ct);
        }, cancellationToken));

    private async Task<Result<T>> WithClinicAsync<T>(Func<Guid, Task<Result<T>>> work) =>
        currentUser.ClinicId is { } clinicId
            ? await work(clinicId)
            : Result<T>.Fail(ResultStatus.Forbidden, "Clinic context is required.");

    /// <summary>Settings are created on first use, with the currency and tooth numbering of the clinic's country.</summary>
    private async Task<ClinicBillingSettings> GetOrCreateSettingsAsync(Guid clinicId, CancellationToken cancellationToken)
    {
        var settings = await billing.GetSettingsAsync(cancellationToken);
        if (settings is not null) return settings;

        return await unitOfWork.RunWithLockAsync($"billing-settings:{clinicId:N}", async ct =>
        {
            var existing = await billing.GetSettingsAsync(ct);
            if (existing is not null) return existing;

            var clinic = await clinics.GetByIdAsync(clinicId, ct);
            var country = clinic?.Country ?? "US";
            var created = new ClinicBillingSettings
            {
                ClinicId = clinicId,
                Currency = CurrencyRules.ForCountry(country),
                ToothNumbering = string.Equals(country, "US", StringComparison.OrdinalIgnoreCase) ? ToothNumbering.Universal : ToothNumbering.Fdi
            };
            billing.AddSettings(created);
            await unitOfWork.SaveChangesAsync(ct);
            return created;
        }, cancellationToken);
    }

    private async Task<Result<InvoiceDto>?> CheckAppointmentAsync(Guid appointmentId, Guid patientId, CancellationToken cancellationToken)
    {
        var appointment = await appointments.GetByIdAsync(appointmentId, cancellationToken);
        if (appointment is null) return Result<InvoiceDto>.Fail(ResultStatus.NotFound, "Appointment was not found.");
        if (appointment.PatientId != patientId)
            return Result<InvoiceDto>.Fail(ResultStatus.ValidationError, "The appointment is for a different patient.");
        if (appointment.Status != AppointmentStatus.Completed)
            return Result<InvoiceDto>.Fail(ResultStatus.Conflict, "Only completed appointments can be invoiced.");
        if (await billing.HasOpenInvoiceForAppointmentAsync(appointmentId, cancellationToken))
            return Result<InvoiceDto>.Fail(ResultStatus.Conflict, "This appointment already has an invoice.");
        return null;
    }

    private async Task<Result<InvoiceDto>?> CheckServicesAsync(IEnumerable<InvoiceLineRequest> lines, CancellationToken cancellationToken)
    {
        foreach (var serviceId in lines.Where(x => x.ServiceId.HasValue).Select(x => x.ServiceId!.Value).Distinct())
        {
            if (await services.GetByIdAsync(serviceId, cancellationToken) is null)
                return Result<InvoiceDto>.Fail(ResultStatus.ValidationError, "A line refers to a service that doesn't exist.");
        }
        return null;
    }

    private void SetLines(Invoice invoice, IReadOnlyList<InvoiceLineRequest> requests, decimal? coveragePercent)
    {
        for (var i = 0; i < requests.Count; i++)
        {
            var r = requests[i];
            var lineTotal = CurrencyRules.Round(r.Quantity * r.UnitPrice, invoice.Currency);
            var line = new InvoiceLine
            {
                ClinicId = invoice.ClinicId,
                InvoiceId = invoice.Id,
                ServiceId = r.ServiceId,
                SortOrder = i,
                Description = r.Description.Trim(),
                ProcedureCode = Optional(r.ProcedureCode)?.ToUpperInvariant(),
                Tooth = Optional(r.Tooth)?.ToUpperInvariant(),
                Quantity = r.Quantity,
                UnitPrice = CurrencyRules.Round(r.UnitPrice, invoice.Currency),
                InsuranceEstimate = coveragePercent.HasValue ? lineTotal * coveragePercent.Value / 100m : r.InsuranceEstimate
            };
            invoice.Lines.Add(line);
            billing.AddLine(line);
        }
    }

    private async Task<TimeZoneInfo> ClinicZoneAsync(Guid clinicId, CancellationToken cancellationToken) =>
        SchedulingEngine.ResolveTimeZone((await clinics.GetByIdAsync(clinicId, cancellationToken))?.TimeZone);

    private DateOnly Today(TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc), zone));

    private static Result<InvoiceDto> NotFound() => Result<InvoiceDto>.Fail(ResultStatus.NotFound, "Invoice was not found.");

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static BillingSettingsDto ToDto(ClinicBillingSettings s) => new(
        s.Currency, CurrencyRules.MinorUnits(s.Currency), s.InvoicePrefix, s.NextInvoiceNumber, s.PaymentTermsDays,
        s.DefaultTaxRatePercent, s.ToothNumbering);

    private static bool IsOverdue(Invoice x, DateOnly today) =>
        x.Status is InvoiceStatus.Issued or InvoiceStatus.PartiallyPaid && x.DueDate < today;

    private static string NameOf(Invoice x) => x.Patient is null ? string.Empty : $"{x.Patient.FirstName} {x.Patient.LastName}".Trim();

    private static InvoiceSummaryDto ToSummary(Invoice x, DateOnly today) => new(
        x.Id, x.Number, x.Status, x.PatientId, NameOf(x), x.AppointmentId, x.IssueDate, x.DueDate, IsOverdue(x, today),
        x.Currency, x.Total, x.AmountPaid, x.Balance, x.CreatedDate);

    private static InvoiceDto ToDto(Invoice x, DateOnly today) => new(
        x.Id, x.Number, x.Status, x.PatientId, NameOf(x), x.AppointmentId, x.IssueDate, x.DueDate, IsOverdue(x, today),
        x.Currency, CurrencyRules.MinorUnits(x.Currency), x.Subtotal, x.DiscountAmount, x.TaxRatePercent, x.TaxAmount, x.Total,
        x.InsuranceEstimate, x.PatientResponsibility, x.AmountPaid, x.Balance, x.Notes, x.VoidReason, x.VoidedAtUtc, x.CreatedDate,
        x.Lines.Where(l => !l.IsDeleted).OrderBy(l => l.SortOrder)
            .Select(l => new InvoiceLineDto(l.Id, l.ServiceId, l.Description, l.ProcedureCode, l.Tooth, l.Quantity, l.UnitPrice,
                l.LineTotal, l.InsuranceEstimate)).ToList(),
        x.Payments.Where(p => !p.IsDeleted).OrderBy(p => p.ReceivedAtUtc)
            .Select(p => new PaymentDto(p.Id, p.Amount, p.Method, p.Reference, DateTime.SpecifyKind(p.ReceivedAtUtc, DateTimeKind.Utc),
                p.Note, p.RefundedAmount, p.CreatedBy,
                p.Refunds.Where(r => !r.IsDeleted).OrderBy(r => r.RefundedAtUtc)
                    .Select(r => new RefundDto(r.Id, r.Amount, r.Reason, DateTime.SpecifyKind(r.RefundedAtUtc, DateTimeKind.Utc), r.CreatedBy))
                    .ToList()))
            .ToList());
}
