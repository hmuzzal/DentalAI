using System.Net.Mail;
using DentalAI.Application.Common.Interfaces;
using DentalAI.Application.Common.Interfaces.Persistence;
using DentalAI.Application.Common.Models;
using DentalAI.Application.Common.Phone;
using DentalAI.Application.Common.Portal;
using DentalAI.Application.Common.Security;
using DentalAI.Application.DTOs.Appointments;
using DentalAI.Application.DTOs.Billing;
using DentalAI.Application.DTOs.Portal;
using DentalAI.Application.Interfaces;
using DentalAI.Domain.Entities;
using DentalAI.Domain.Enums;

namespace DentalAI.Application.Services;

/// <summary>
/// The patient portal: passwordless sign-in (a code by email or text), the patient's own profile, booking,
/// rescheduling and cancelling through the same engine as the front desk, and their invoices.
/// Everything a signed-in patient reads or changes is limited to their own records.
/// </summary>
public sealed class PortalService(
    IPortalRepository portal,
    IPatientRepository patients,
    IServiceRepository services,
    IDoctorRepository doctors,
    IAppointmentRepository appointmentRepository,
    ISchedulingService scheduling,
    IAppointmentService appointments,
    BillingService billing,
    NotificationService notifications,
    IJwtTokenGenerator jwt,
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock)
{
    private const string PortalUnavailableMessage = "This clinic's online booking isn't available.";
    private const string WrongCodeMessage = "That code is wrong or has expired. Check it, or ask for a new one.";
    private const string TicketExpiredMessage = "Your sign-in has expired. Please start again.";
    private const string SignedOutMessage = "Your session has ended. Please sign in again.";

    // ---- Public ----

    public async Task<Result<PortalClinicDto>> GetClinicAsync(string slug, CancellationToken cancellationToken) =>
        await OpenPortalAsync(slug, cancellationToken) is { } found
            ? Result<PortalClinicDto>.Ok(ToClinicDto(found.Settings, found.Clinic))
            : Unavailable<PortalClinicDto>();

    /// <summary>
    /// Sends a code to the email or phone. The answer is the same whether or not it belongs to a patient, so the
    /// portal can't be used to find out who is a patient of the clinic.
    /// </summary>
    public async Task<Result<PortalStartResponse>> StartSignInAsync(string slug, PortalStartRequest request, CancellationToken cancellationToken)
    {
        if (await OpenPortalAsync(slug, cancellationToken) is not { } found) return Unavailable<PortalStartResponse>();
        var (settings, clinic) = found;
        if (!TryReadContact(request.Contact, clinic.Country, out var channel, out var contact))
            return Result<PortalStartResponse>.Fail(ResultStatus.ValidationError, "Enter a valid email address or mobile number.");

        var response = new PortalStartResponse(channel, contact, PortalRules.CodeValidMinutes);
        var sentMessage = $"If {contact} is on file with {clinic.Name}, we've sent a sign-in code to it.";

        return await unitOfWork.RunWithLockAsync(ContactLock(clinic.Id, contact), async ct =>
        {
            var now = clock.UtcNow;
            if (await portal.CountCodesSinceAsync(clinic.Id, contact, now - PortalRules.CodeWindow, ct) >= PortalRules.MaxCodesPerWindow)
                return Result<PortalStartResponse>.Fail(ResultStatus.TooManyRequests,
                    $"Too many codes were requested. Please wait {PortalRules.CodeWindow.TotalMinutes:0} minutes and try again.");

            var matches = await portal.FindPatientsByContactAsync(clinic.Id, contact, channel, ct);
            if (matches.Count == 0 && !settings.AllowNewPatients) return Result<PortalStartResponse>.Ok(response, sentMessage);

            var code = PortalRules.NewCode();
            var row = new PortalSignInCode
            {
                ClinicId = clinic.Id,
                Channel = channel,
                Contact = contact,
                ExpiresAtUtc = now.AddMinutes(PortalRules.CodeValidMinutes)
            };
            row.CodeHash = PortalRules.HashCode(row.Id, code);
            portal.AddCode(row);
            await portal.SaveChangesAsync(ct);

            await notifications.QueuePortalCodeAsync(clinic.Id, clinic.Name, channel, contact, code, PortalRules.CodeValidMinutes,
                matches.Count == 1 ? matches[0].Id : null, ct);
            return Result<PortalStartResponse>.Ok(response, sentMessage);
        }, cancellationToken);
    }

    /// <summary>Checks the code. Five wrong tries use it up. A right code returns a ticket and who it can sign in as.</summary>
    public async Task<Result<PortalVerifyResponse>> VerifyCodeAsync(string slug, PortalVerifyRequest request, CancellationToken cancellationToken)
    {
        if (await OpenPortalAsync(slug, cancellationToken) is not { } found) return Unavailable<PortalVerifyResponse>();
        var (settings, clinic) = found;
        if (!TryReadContact(request.Contact, clinic.Country, out var channel, out var contact))
            return Result<PortalVerifyResponse>.Fail(ResultStatus.ValidationError, WrongCodeMessage);

        // Under the contact's lock, so parallel guesses can't get around the attempt limit.
        return await unitOfWork.RunWithLockAsync(ContactLock(clinic.Id, contact), async ct =>
        {
            var now = clock.UtcNow;
            var row = await portal.GetOpenCodeAsync(clinic.Id, contact, now, PortalRules.MaxCodeAttempts, ct);
            if (row is null) return Result<PortalVerifyResponse>.Fail(ResultStatus.ValidationError, WrongCodeMessage);

            if (!PortalRules.HashEquals(row.CodeHash, PortalRules.HashCode(row.Id, request.Code ?? string.Empty)))
            {
                row.FailedAttempts++;
                await portal.SaveChangesAsync(ct);
                return Result<PortalVerifyResponse>.Fail(ResultStatus.ValidationError, WrongCodeMessage);
            }

            var (ticket, ticketHash) = InviteTokens.Create();
            row.VerifiedAtUtc = now;
            row.TicketHash = ticketHash;
            row.TicketExpiresAtUtc = now + PortalRules.TicketLifetime;
            await portal.SaveChangesAsync(ct);

            var matches = await portal.FindPatientsByContactAsync(clinic.Id, contact, channel, ct);
            return Result<PortalVerifyResponse>.Ok(new PortalVerifyResponse(
                ticket, row.TicketExpiresAtUtc.Value, channel, contact,
                matches.Select(p => new PortalPatientChoiceDto(p.Id, PortalRules.DisplayName(p.FirstName, p.LastName))).ToList(),
                settings.AllowNewPatients));
        }, cancellationToken);
    }

    /// <summary>Signs in as one of the patients the verified contact belongs to.</summary>
    public async Task<Result<PortalSessionDto>> SelectPatientAsync(string slug, PortalSelectRequest request, CancellationToken cancellationToken)
    {
        if (await OpenPortalAsync(slug, cancellationToken) is not { } found) return Unavailable<PortalSessionDto>();
        var (settings, clinic) = found;
        var ticket = await RedeemableTicketAsync(clinic.Id, request.Ticket, cancellationToken);
        if (ticket is null) return Result<PortalSessionDto>.Fail(ResultStatus.Unauthorized, TicketExpiredMessage);

        var matches = await portal.FindPatientsByContactAsync(clinic.Id, ticket.Contact, ticket.Channel, cancellationToken);
        var patient = matches.FirstOrDefault(p => p.Id == request.PatientId);
        if (patient is null) return Result<PortalSessionDto>.Fail(ResultStatus.NotFound, "That patient isn't linked to this email or phone.");

        ticket.UsedAtUtc = clock.UtcNow;
        return Result<PortalSessionDto>.Ok(await StartSessionAsync(patient, settings, clinic, cancellationToken));
    }

    /// <summary>Creates a patient for the verified contact (when the clinic takes new patients online) and signs them in.</summary>
    public async Task<Result<PortalSessionDto>> RegisterAsync(string slug, PortalRegisterRequest request, CancellationToken cancellationToken)
    {
        if (await OpenPortalAsync(slug, cancellationToken) is not { } found) return Unavailable<PortalSessionDto>();
        var (settings, clinic) = found;
        if (!settings.AllowNewPatients)
            return Result<PortalSessionDto>.Fail(ResultStatus.Forbidden,
                $"{clinic.Name} doesn't register new patients online.{CallClinic(clinic)}");

        var ticket = await RedeemableTicketAsync(clinic.Id, request.Ticket, cancellationToken);
        if (ticket is null) return Result<PortalSessionDto>.Fail(ResultStatus.Unauthorized, TicketExpiredMessage);

        // The verified contact is used as is; the other one comes from the form.
        string phone;
        string? email;
        if (ticket.Channel == MessageChannel.Sms)
        {
            phone = ticket.Contact;
            email = Optional(request.Email)?.ToLowerInvariant();
            if (email is not null && !IsEmail(email))
                return Result<PortalSessionDto>.Fail(ResultStatus.ValidationError, "Enter a valid email address.");
        }
        else
        {
            email = ticket.Contact;
            if (!PhoneNumberRules.TryNormalize(request.Phone, clinic.Country, out phone))
                return Result<PortalSessionDto>.Fail(ResultStatus.ValidationError, "The phone number is not valid.",
                    [PhoneNumberRules.InvalidMessage(clinic.Country)]);
        }

        var existing = await portal.FindPatientsByContactAsync(clinic.Id, ticket.Contact, ticket.Channel, cancellationToken);
        if (existing.Any(p => p.DateOfBirth == request.DateOfBirth
                && string.Equals(p.FirstName.Trim(), request.FirstName.Trim(), StringComparison.OrdinalIgnoreCase)))
            return Result<PortalSessionDto>.Fail(ResultStatus.Conflict,
                "You're already registered with this clinic. Go back and choose your name.");

        var now = clock.UtcNow;
        var patient = new Patient
        {
            ClinicId = clinic.Id,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            DateOfBirth = request.DateOfBirth,
            Phone = phone,
            Email = email,
            PreferredContactMethod = request.SmsOptIn ? ContactMethod.Sms : email is not null ? ContactMethod.Email : ContactMethod.Phone,
            SmsOptIn = request.SmsOptIn,
            SmsOptInAtUtc = request.SmsOptIn ? now : null,
            Notes = "Registered on the patient portal."
        };
        portal.AddPatient(patient);
        ticket.UsedAtUtc = now;
        return Result<PortalSessionDto>.Ok(await StartSessionAsync(patient, settings, clinic, cancellationToken),
            $"Welcome to {clinic.Name}.");
    }

    /// <summary>A new access token for a refresh token. The refresh token is replaced each time.</summary>
    public async Task<Result<PortalSessionDto>> RefreshAsync(PortalRefreshRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken)) return Result<PortalSessionDto>.Fail(ResultStatus.Unauthorized, SignedOutMessage);

        var now = clock.UtcNow;
        var session = await portal.GetSessionAsync(InviteTokens.Hash(request.RefreshToken.Trim()), cancellationToken);
        if (session is null || session.RevokedAtUtc is not null || session.ExpiresAtUtc <= now
            || session.Patient is null || session.Patient.IsDeleted || session.ClinicId is not { } clinicId)
            return Result<PortalSessionDto>.Fail(ResultStatus.Unauthorized, SignedOutMessage);

        var settings = await portal.GetSettingsForClinicAsync(clinicId, cancellationToken);
        var clinic = await portal.GetClinicAsync(clinicId, cancellationToken);
        if (settings is not { Enabled: true } || clinic is null)
            return Result<PortalSessionDto>.Fail(ResultStatus.Unauthorized, PortalUnavailableMessage);

        var (refreshToken, refreshHash) = InviteTokens.Create();
        session.TokenHash = refreshHash;
        session.LastUsedAtUtc = now;
        session.ExpiresAtUtc = now + jwt.PatientRefreshTokenLifetime;
        await portal.SaveChangesAsync(cancellationToken);

        var (accessToken, accessExpires) = jwt.GeneratePatientAccessToken(session.Patient);
        return Result<PortalSessionDto>.Ok(new PortalSessionDto(accessToken, accessExpires, refreshToken, session.ExpiresAtUtc,
            ToSummary(session.Patient), ToClinicDto(settings, clinic)));
    }

    /// <summary>Ends the session. Always succeeds, so it can't be used to test tokens.</summary>
    public async Task<Result> SignOutAsync(PortalRefreshRequest request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            var session = await portal.GetSessionAsync(InviteTokens.Hash(request.RefreshToken.Trim()), cancellationToken);
            if (session is { RevokedAtUtc: null })
            {
                session.RevokedAtUtc = clock.UtcNow;
                await portal.SaveChangesAsync(cancellationToken);
            }
        }

        return Result.Ok("You're signed out.");
    }

    // ---- Signed-in patient: profile ----

    public Task<Result<PortalProfileDto>> GetProfileAsync(CancellationToken cancellationToken) =>
        AsPatientAsync(context => Task.FromResult(Result<PortalProfileDto>.Ok(ToProfile(context))), cancellationToken);

    public Task<Result<PortalProfileDto>> UpdateProfileAsync(PortalUpdateProfileRequest request, CancellationToken cancellationToken) =>
        AsPatientAsync(async context =>
        {
            if (!PhoneNumberRules.TryNormalize(request.Phone, context.Clinic.Country, out var phone))
                return Result<PortalProfileDto>.Fail(ResultStatus.ValidationError, "The phone number is not valid.",
                    [PhoneNumberRules.InvalidMessage(context.Clinic.Country)]);
            var email = Optional(request.Email)?.ToLowerInvariant();
            if (email is not null && !IsEmail(email))
                return Result<PortalProfileDto>.Fail(ResultStatus.ValidationError, "Enter a valid email address.");

            var patient = context.Patient;
            patient.Phone = phone;
            patient.Email = email;
            patient.AddressLine1 = Optional(request.AddressLine1);
            patient.AddressLine2 = Optional(request.AddressLine2);
            patient.City = Optional(request.City);
            patient.State = Optional(request.State)?.ToUpperInvariant();
            patient.PostalCode = Optional(request.PostalCode)?.ToUpperInvariant();
            patient.PreferredContactMethod = request.PreferredContactMethod;

            // TCPA: keep a record of when consent was given or withdrawn.
            if (request.SmsOptIn && !patient.SmsOptIn)
            {
                patient.SmsOptInAtUtc = clock.UtcNow;
                patient.SmsOptOutAtUtc = null;
            }
            else if (!request.SmsOptIn && patient.SmsOptIn)
            {
                patient.SmsOptOutAtUtc = clock.UtcNow;
            }
            patient.SmsOptIn = request.SmsOptIn;

            patients.Update(patient);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<PortalProfileDto>.Ok(ToProfile(context), "Your details are saved.");
        }, cancellationToken);

    // ---- Signed-in patient: booking ----

    public Task<Result<IReadOnlyList<PortalServiceDto>>> ListServicesAsync(CancellationToken cancellationToken) =>
        AsPatientAsync(async _ =>
        {
            var list = (await services.ListAsync(cancellationToken))
                .Where(s => s.IsActive && s.BookableOnline)
                .OrderBy(s => s.Name)
                .Select(s => new PortalServiceDto(s.Id, s.Name, s.Description, s.DurationMinutes))
                .ToList();
            return Result<IReadOnlyList<PortalServiceDto>>.Ok(list);
        }, cancellationToken);

    public Task<Result<IReadOnlyList<PortalDoctorDto>>> ListDoctorsAsync(CancellationToken cancellationToken) =>
        AsPatientAsync(async context =>
        {
            var list = (await doctors.ListAsync(context.Clinic.Id, cancellationToken))
                .OrderBy(d => d.LastName).ThenBy(d => d.FirstName)
                .Select(d => new PortalDoctorDto(d.Id, DoctorName(d), Optional(d.Specialty)))
                .ToList();
            return Result<IReadOnlyList<PortalDoctorDto>>.Ok(list);
        }, cancellationToken);

    /// <summary>
    /// Open times for a doctor, service and clinic-local day, within the clinic's notice and how-far-ahead rules.
    /// <paramref name="rescheduleAppointmentId"/>: one of the patient's appointments, which then doesn't block itself.
    /// </summary>
    public Task<Result<IReadOnlyList<PortalSlotDto>>> GetSlotsAsync(Guid doctorId, Guid serviceId, DateOnly date,
        Guid? rescheduleAppointmentId, CancellationToken cancellationToken) =>
        AsPatientAsync(async context =>
        {
            var service = await services.GetByIdAsync(serviceId, cancellationToken);
            if (service is not { IsActive: true, BookableOnline: true })
                return Result<IReadOnlyList<PortalSlotDto>>.Fail(ResultStatus.NotFound, "This service can't be booked online.");

            if (rescheduleAppointmentId is { } appointmentId)
            {
                var own = await appointmentRepository.GetByIdAsync(appointmentId, cancellationToken);
                if (own is null || own.PatientId != context.Patient.Id)
                    return Result<IReadOnlyList<PortalSlotDto>>.Fail(ResultStatus.NotFound, "Appointment was not found.");
            }

            var today = Today(context);
            if (date < today || date > today.AddDays(context.Settings.MaxDaysAhead))
                return Result<IReadOnlyList<PortalSlotDto>>.Ok([]);

            var slots = await scheduling.GetAvailableSlotsAsync(doctorId, date, serviceId, cancellationToken, rescheduleAppointmentId);
            if (!slots.Success) return Result<IReadOnlyList<PortalSlotDto>>.Fail(slots.Status, slots.Message, slots.Errors);

            var earliest = clock.UtcNow.AddHours(context.Settings.MinNoticeHours);
            return Result<IReadOnlyList<PortalSlotDto>>.Ok(slots.Data!
                .Where(s => s.StartTimeUtc >= earliest)
                .Select(s => new PortalSlotDto(s.StartTimeUtc, s.EndTimeUtc))
                .ToList());
        }, cancellationToken);

    public Task<Result<IReadOnlyList<PortalAppointmentDto>>> ListAppointmentsAsync(CancellationToken cancellationToken) =>
        AsPatientAsync(async context =>
        {
            var list = await portal.ListAppointmentsAsync(context.Patient.Id, 100, cancellationToken);
            return Result<IReadOnlyList<PortalAppointmentDto>>.Ok(list.Select(a => ToPortal(a, context)).ToList());
        }, cancellationToken);

    /// <summary>Books through the scheduling engine, under the doctor's lock, like the front desk.</summary>
    public Task<Result<PortalAppointmentDto>> BookAsync(PortalBookRequest request, CancellationToken cancellationToken) =>
        AsPatientAsync(async context =>
        {
            var service = await services.GetByIdAsync(request.ServiceId, cancellationToken);
            if (service is not { IsActive: true, BookableOnline: true })
                return Result<PortalAppointmentDto>.Fail(ResultStatus.NotFound, "This service can't be booked online.");

            var start = AsUtc(request.StartTime);
            if (CheckWindow(start, context) is { } windowError)
                return Result<PortalAppointmentDto>.Fail(ResultStatus.ValidationError, windowError);

            var limit = context.Settings.MaxUpcomingAppointments;
            if (await portal.CountUpcomingAsync(context.Patient.Id, clock.UtcNow, cancellationToken) >= limit)
                return Result<PortalAppointmentDto>.Fail(ResultStatus.Conflict,
                    $"You already have {limit} upcoming appointment{(limit == 1 ? "" : "s")}, the most you can book online.{CallClinic(context.Clinic)}");

            var note = Optional(request.Note);
            var created = await appointments.CreateAppointmentAsync(new CreateAppointmentRequest
            {
                DoctorId = request.DoctorId,
                PatientId = context.Patient.Id,
                ServiceId = service.Id,
                StartTime = start,
                Notes = note is null ? null : $"Patient's note (booked online): {note}"
            }, AppointmentSource.Portal, confirmed: context.Settings.AutoConfirm, cancellationToken);
            if (!created.Success) return Result<PortalAppointmentDto>.Fail(created.Status, created.Message, created.Errors);

            return Result<PortalAppointmentDto>.Ok(ToPortal(created.Data!, serviceBookable: true, context),
                context.Settings.AutoConfirm
                    ? "You're booked."
                    : "Request sent. The clinic will confirm your appointment.");
        }, cancellationToken);

    public Task<Result<PortalAppointmentDto>> RescheduleAsync(Guid id, PortalRescheduleRequest request, CancellationToken cancellationToken) =>
        AsPatientAsync(async context =>
        {
            var appointment = await appointmentRepository.GetByIdAsync(id, cancellationToken);
            if (appointment is null || appointment.PatientId != context.Patient.Id)
                return Result<PortalAppointmentDto>.Fail(ResultStatus.NotFound, "Appointment was not found.");

            var current = ToPortal(appointment, context);
            if (!current.CanReschedule) return Result<PortalAppointmentDto>.Fail(ResultStatus.Conflict, CannotChangeMessage(current, context));

            var start = AsUtc(request.StartTime);
            if (CheckWindow(start, context) is { } windowError)
                return Result<PortalAppointmentDto>.Fail(ResultStatus.ValidationError, windowError);

            var moved = await appointments.RescheduleAsync(id, new RescheduleAppointmentRequest { StartTime = start }, cancellationToken);
            return moved.Success
                ? Result<PortalAppointmentDto>.Ok(ToPortal(moved.Data!, serviceBookable: true, context), "Your appointment has been moved.")
                : Result<PortalAppointmentDto>.Fail(moved.Status, moved.Message, moved.Errors);
        }, cancellationToken);

    public Task<Result<PortalAppointmentDto>> CancelAsync(Guid id, CancellationToken cancellationToken) =>
        AsPatientAsync(async context =>
        {
            var appointment = await appointmentRepository.GetByIdAsync(id, cancellationToken);
            if (appointment is null || appointment.PatientId != context.Patient.Id)
                return Result<PortalAppointmentDto>.Fail(ResultStatus.NotFound, "Appointment was not found.");

            var current = ToPortal(appointment, context);
            if (!current.CanCancel) return Result<PortalAppointmentDto>.Fail(ResultStatus.Conflict, CannotChangeMessage(current, context));

            var cancelled = await appointments.CancelAsync(id, new UpdateAppointmentStatusRequest(), cancellationToken);
            return cancelled.Success
                ? Result<PortalAppointmentDto>.Ok(ToPortal(cancelled.Data!, appointment.Service?.BookableOnline ?? false, context),
                    "Your appointment has been cancelled.")
                : Result<PortalAppointmentDto>.Fail(cancelled.Status, cancelled.Message, cancelled.Errors);
        }, cancellationToken);

    // ---- Signed-in patient: invoices ----

    public Task<Result<PortalInvoiceListDto>> ListInvoicesAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        AsPatientAsync(async context =>
        {
            var list = await billing.ListAsync(new InvoiceListQuery
            {
                PatientId = context.Patient.Id,
                IssuedOnly = true,
                Page = page,
                PageSize = pageSize
            }, cancellationToken);
            if (!list.Success) return Result<PortalInvoiceListDto>.Fail(list.Status, list.Message);

            var data = list.Data!;
            return Result<PortalInvoiceListDto>.Ok(new PortalInvoiceListDto(
                data.Items.Select(i => new PortalInvoiceSummaryDto(i.Id, i.Number, i.Status, i.IssueDate, i.DueDate, i.IsOverdue,
                    i.Currency, i.Total, i.AmountPaid, i.Balance)).ToList(),
                data.TotalCount, data.Page, data.PageSize, data.Outstanding));
        }, cancellationToken);

    public Task<Result<PortalInvoiceDto>> GetInvoiceAsync(Guid id, CancellationToken cancellationToken) =>
        AsPatientAsync(async context =>
        {
            var found = await billing.GetAsync(id, cancellationToken);
            if (!found.Success || found.Data!.PatientId != context.Patient.Id
                || found.Data.Status is InvoiceStatus.Draft or InvoiceStatus.Void)
                return Result<PortalInvoiceDto>.Fail(ResultStatus.NotFound, "Invoice was not found.");

            var i = found.Data;
            return Result<PortalInvoiceDto>.Ok(new PortalInvoiceDto(
                i.Id, i.Number, i.Status, i.IssueDate, i.DueDate, i.IsOverdue, i.Currency, i.CurrencyDecimals,
                i.Subtotal, i.DiscountAmount, i.TaxRatePercent, i.TaxAmount, i.Total, i.InsuranceEstimate, i.PatientResponsibility,
                i.AmountPaid, i.Balance, i.Notes,
                i.Lines.Select(l => new PortalInvoiceLineDto(l.Description, l.Tooth, l.Quantity, l.UnitPrice, l.LineTotal)).ToList(),
                i.Payments.Select(p => new PortalPaymentDto(p.Amount, p.Method, p.ReceivedAtUtc, p.RefundedAmount)).ToList()));
        }, cancellationToken);

    // ---- Clinic admins: settings ----

    public async Task<Result<PortalSettingsDto>> GetSettingsAsync(CancellationToken cancellationToken)
    {
        if (currentUser.ClinicId is not { } clinicId) return Result<PortalSettingsDto>.Fail(ResultStatus.Forbidden, "Clinic context is required.");
        return Result<PortalSettingsDto>.Ok(ToSettingsDto(await GetOrCreateSettingsAsync(clinicId, cancellationToken)));
    }

    public async Task<Result<PortalSettingsDto>> UpdateSettingsAsync(UpdatePortalSettingsRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.ClinicId is not { } clinicId) return Result<PortalSettingsDto>.Fail(ResultStatus.Forbidden, "Clinic context is required.");
        var slug = (request.Slug ?? string.Empty).Trim().ToLowerInvariant();
        if (!PortalRules.IsValidSlug(slug))
            return Result<PortalSettingsDto>.Fail(ResultStatus.ValidationError,
                $"The address must be {PortalRules.SlugMinLength}–{PortalRules.SlugMaxLength} lowercase letters, digits or dashes.");

        var settings = await GetOrCreateSettingsAsync(clinicId, cancellationToken);
        return await unitOfWork.RunWithLockAsync("portal-slugs", async ct =>
        {
            if (await portal.SlugTakenAsync(slug, clinicId, ct))
                return Result<PortalSettingsDto>.Fail(ResultStatus.Conflict, "Another clinic already uses that address. Choose another.");

            settings.Enabled = request.Enabled;
            settings.Slug = slug;
            settings.AllowNewPatients = request.AllowNewPatients;
            settings.AutoConfirm = request.AutoConfirm;
            settings.MinNoticeHours = request.MinNoticeHours;
            settings.MaxDaysAhead = request.MaxDaysAhead;
            settings.CancelCutoffHours = request.CancelCutoffHours;
            settings.MaxUpcomingAppointments = request.MaxUpcomingAppointments;
            await unitOfWork.SaveChangesAsync(ct);
            return Result<PortalSettingsDto>.Ok(ToSettingsDto(settings),
                settings.Enabled ? "Patient portal settings saved. The portal is on." : "Patient portal settings saved. The portal is off.");
        }, cancellationToken);
    }

    /// <summary>Created on first use, switched off, with an address made from the clinic's name.</summary>
    private async Task<ClinicPortalSettings> GetOrCreateSettingsAsync(Guid clinicId, CancellationToken cancellationToken)
    {
        var settings = await portal.GetSettingsAsync(cancellationToken);
        if (settings is not null) return settings;

        return await unitOfWork.RunWithLockAsync("portal-slugs", async ct =>
        {
            var existing = await portal.GetSettingsAsync(ct);
            if (existing is not null) return existing;

            var clinic = await portal.GetClinicAsync(clinicId, ct);
            var baseSlug = PortalRules.SlugFrom(clinic?.Name ?? "clinic");
            var slug = baseSlug;
            for (var n = 2; await portal.SlugTakenAsync(slug, clinicId, ct); n++) slug = $"{baseSlug}-{n}";

            var created = new ClinicPortalSettings { ClinicId = clinicId, Slug = slug };
            portal.AddSettings(created);
            await unitOfWork.SaveChangesAsync(ct);
            return created;
        }, cancellationToken);
    }

    // ---- Helpers ----

    private sealed record PatientContext(Patient Patient, ClinicPortalSettings Settings, Clinic Clinic, TimeZoneInfo Zone);

    /// <summary>
    /// Loads the signed-in patient, their clinic and its portal rules. A deleted patient, or a portal switched off
    /// since the token was issued, ends the session (401) instead of waiting for the token to expire.
    /// </summary>
    private async Task<Result<T>> AsPatientAsync<T>(Func<PatientContext, Task<Result<T>>> work, CancellationToken cancellationToken)
    {
        if (currentUser.PatientId is not { } patientId || currentUser.ClinicId is not { } clinicId)
            return Result<T>.Fail(ResultStatus.Unauthorized, SignedOutMessage);

        var settings = await portal.GetSettingsAsync(cancellationToken);
        if (settings is not { Enabled: true }) return Result<T>.Fail(ResultStatus.Unauthorized, PortalUnavailableMessage);

        var patient = await patients.GetByIdAsync(patientId, cancellationToken);
        var clinic = await portal.GetClinicAsync(clinicId, cancellationToken);
        if (patient is null || clinic is null) return Result<T>.Fail(ResultStatus.Unauthorized, SignedOutMessage);

        return await work(new PatientContext(patient, settings, clinic, SchedulingEngine.ResolveTimeZone(clinic.TimeZone)));
    }

    private async Task<(ClinicPortalSettings Settings, Clinic Clinic)?> OpenPortalAsync(string? slug, CancellationToken cancellationToken)
    {
        var normalized = (slug ?? string.Empty).Trim().ToLowerInvariant();
        if (!PortalRules.IsValidSlug(normalized)) return null;
        var found = await portal.GetBySlugAsync(normalized, cancellationToken);
        return found is { } f && f.Settings.Enabled ? f : null;
    }

    /// <summary>A verified, unused, unexpired ticket. Tracked, so the caller can mark it used.</summary>
    private async Task<PortalSignInCode?> RedeemableTicketAsync(Guid clinicId, string? ticket, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ticket)) return null;
        var row = await portal.GetByTicketHashAsync(clinicId, InviteTokens.Hash(ticket.Trim()), cancellationToken);
        return row is { VerifiedAtUtc: not null, UsedAtUtc: null, TicketExpiresAtUtc: { } expires } && expires > clock.UtcNow ? row : null;
    }

    private async Task<PortalSessionDto> StartSessionAsync(Patient patient, ClinicPortalSettings settings, Clinic clinic,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var (refreshToken, refreshHash) = InviteTokens.Create();
        var session = new PatientPortalSession
        {
            ClinicId = patient.ClinicId,
            PatientId = patient.Id,
            TokenHash = refreshHash,
            ExpiresAtUtc = now + jwt.PatientRefreshTokenLifetime,
            LastUsedAtUtc = now
        };
        portal.AddSession(session);
        await portal.SaveChangesAsync(cancellationToken);

        var (accessToken, accessExpires) = jwt.GeneratePatientAccessToken(patient);
        return new PortalSessionDto(accessToken, accessExpires, refreshToken, session.ExpiresAtUtc, ToSummary(patient),
            ToClinicDto(settings, clinic));
    }

    /// <summary>An email address (lowercased) or a phone number (E.164, read in the clinic's country).</summary>
    private static bool TryReadContact(string? input, string? country, out MessageChannel channel, out string contact)
    {
        channel = MessageChannel.Email;
        contact = string.Empty;
        var trimmed = (input ?? string.Empty).Trim();
        if (trimmed.Length is 0 or > 200) return false;

        if (trimmed.Contains('@'))
        {
            if (!IsEmail(trimmed)) return false;
            contact = trimmed.ToLowerInvariant();
            return true;
        }

        if (!PhoneNumberRules.TryNormalize(trimmed, country, out var e164)) return false;
        channel = MessageChannel.Sms;
        contact = e164;
        return true;
    }

    private static bool IsEmail(string value) =>
        MailAddress.TryCreate(value, out var address) && address.Address == value && value.Contains('.');

    /// <summary>One lock per clinic and contact (hashed, to keep the lock name short).</summary>
    private static string ContactLock(Guid clinicId, string contact) =>
        $"portal-contact:{clinicId:N}:{InviteTokens.Hash(contact)[..24]}";

    private string? CheckWindow(DateTime startUtc, PatientContext context)
    {
        var settings = context.Settings;
        if (startUtc < clock.UtcNow.AddHours(settings.MinNoticeHours))
            return settings.MinNoticeHours == 0
                ? "That time has passed. Choose another."
                : $"Online bookings need at least {settings.MinNoticeHours} hours' notice.{CallClinic(context.Clinic)}";

        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(startUtc, context.Zone));
        return localDate > Today(context).AddDays(settings.MaxDaysAhead)
            ? $"You can book up to {settings.MaxDaysAhead} days ahead."
            : null;
    }

    private string CannotChangeMessage(PortalAppointmentDto appointment, PatientContext context)
    {
        if (appointment.Status is not (AppointmentStatus.Pending or AppointmentStatus.Confirmed))
            return "This appointment can no longer be changed.";
        if (clock.UtcNow >= appointment.ChangeDeadlineUtc)
            return $"Changes less than {context.Settings.CancelCutoffHours} hours before the visit can't be made online.{CallClinic(context.Clinic)}";
        return $"This appointment can't be moved online.{CallClinic(context.Clinic)}";
    }

    private DateOnly Today(PatientContext context) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(clock.UtcNow, context.Zone));

    private PortalAppointmentDto ToPortal(Appointment a, PatientContext context) =>
        Build(a.Id, a.DoctorId, a.Doctor is null ? string.Empty : DoctorName(a.Doctor), a.ServiceId, a.Service?.Name ?? string.Empty,
            a.StartTime, a.EndTime, a.Status, a.Source, a.Service?.BookableOnline == true && a.Service.IsActive, context);

    private PortalAppointmentDto ToPortal(AppointmentDto a, bool serviceBookable, PatientContext context) =>
        Build(a.Id, a.DoctorId, string.IsNullOrWhiteSpace(a.DoctorName) ? string.Empty : $"Dr. {a.DoctorName}", a.ServiceId, a.ServiceName,
            a.StartTime, a.EndTime, a.Status, a.Source, serviceBookable, context);

    private PortalAppointmentDto Build(Guid id, Guid doctorId, string doctorName, Guid serviceId, string serviceName,
        DateTime start, DateTime end, AppointmentStatus status, AppointmentSource source, bool serviceBookable, PatientContext context)
    {
        var startUtc = AsUtc(start);
        var deadline = startUtc.AddHours(-context.Settings.CancelCutoffHours);
        var open = status is AppointmentStatus.Pending or AppointmentStatus.Confirmed && clock.UtcNow < deadline;
        return new PortalAppointmentDto(id, doctorId, doctorName, serviceId, serviceName, startUtc, AsUtc(end), status, source,
            context.Clinic.TimeZone, open, open && serviceBookable, deadline);
    }

    private static PortalProfileDto ToProfile(PatientContext context)
    {
        var p = context.Patient;
        return new PortalProfileDto(p.Id, p.FirstName, p.LastName, p.DateOfBirth, p.Phone, p.Email, p.AddressLine1, p.AddressLine2,
            p.City, p.State, p.PostalCode, p.PreferredContactMethod, p.SmsOptIn, ToClinicDto(context.Settings, context.Clinic));
    }

    private static PortalClinicDto ToClinicDto(ClinicPortalSettings s, Clinic c) => new(
        c.Name, s.Slug, Optional(c.Phone), Optional(c.Address), c.TimeZone, s.AllowNewPatients, s.MinNoticeHours, s.MaxDaysAhead,
        s.CancelCutoffHours);

    private static PortalSettingsDto ToSettingsDto(ClinicPortalSettings s) => new(
        s.Enabled, s.Slug, $"/p/{s.Slug}", s.AllowNewPatients, s.AutoConfirm, s.MinNoticeHours, s.MaxDaysAhead, s.CancelCutoffHours,
        s.MaxUpcomingAppointments);

    private static PortalPatientSummaryDto ToSummary(Patient p) => new(p.Id, p.FirstName, p.LastName);

    private static string DoctorName(Doctor d) => $"Dr. {d.FirstName} {d.LastName}".Trim();

    private static string CallClinic(Clinic clinic) =>
        string.IsNullOrWhiteSpace(clinic.Phone) ? string.Empty : $" Please call {PhoneNumberRules.Display(clinic.Phone)}.";

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Result<T> Unavailable<T>() => Result<T>.Fail(ResultStatus.NotFound, PortalUnavailableMessage);
}
