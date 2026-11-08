using System.Net;
using System.Net.Http.Json;
using DentalAI.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace DentalAI.Api.IntegrationTests;

public sealed record BillingSettings(string Currency, int CurrencyDecimals, string InvoicePrefix, int PaymentTermsDays,
    decimal DefaultTaxRatePercent, string ToothNumbering);
public sealed record InvoiceLine(Guid Id, string Description, int Quantity, decimal UnitPrice, decimal LineTotal, decimal InsuranceEstimate);
public sealed record InvoiceRefund(Guid Id, decimal Amount, string Reason);
public sealed record InvoicePayment(Guid Id, decimal Amount, string Method, decimal RefundedAmount, List<InvoiceRefund> Refunds);
public sealed record Invoice(Guid Id, string? Number, string Status, Guid PatientId, Guid? AppointmentId, DateOnly? IssueDate,
    DateOnly? DueDate, string Currency, decimal Subtotal, decimal TaxAmount, decimal Total, decimal InsuranceEstimate,
    decimal PatientResponsibility, decimal AmountPaid, decimal Balance, string? VoidReason, List<InvoiceLine> Lines,
    List<InvoicePayment> Payments);
public sealed record InvoiceSummary(Guid Id, string? Number, string Status, decimal Balance, string Currency);
public sealed record CurrencyAmount(string Currency, decimal Amount);
public sealed record InvoiceList(List<InvoiceSummary> Items, int TotalCount, List<CurrencyAmount> Outstanding);
public sealed record RevenuePoint(string Period, decimal Revenue);
public sealed record SchedulingDashboard(List<RevenuePoint>? RevenueByDay);
public sealed record InvoicePartyDoc(string Name, string? AddressLine1, string? CityLine);
public sealed record InvoiceDocument(Invoice Invoice, InvoicePartyDoc Clinic, InvoicePartyDoc Patient, string ToothNumbering);

/// <summary>Phase 2a: invoices, payments, refunds and voids, with no payment provider.</summary>
[Collection(ApiCollection.Name)]
public sealed class BillingApiTests(ApiFactory factory)
{
    // ---- Settings ----

    [Theory]
    [InlineData("US", "America/New_York", "USD", "Universal")]
    [InlineData("BD", "Asia/Dhaka", "BDT", "Fdi")]
    public async Task Settings_DefaultFromTheClinicsCountry(string country, string zone, string currency, string numbering)
    {
        var clinic = await TestClinics.CreateAsync(factory, country, zone);

        var settings = await SchedulingApi.ReadAsync<BillingSettings>(await clinic.Receptionist.Http.GetAsync("/api/billing/settings"));

        Assert.Equal(currency, settings.Currency);
        Assert.Equal(numbering, settings.ToothNumbering);
        Assert.Equal(0m, settings.DefaultTaxRatePercent);
    }

    [Fact]
    public async Task Settings_OnlyAdminsChangeThem_AndExistingInvoicesKeepTheirCurrency()
    {
        var clinic = await TestClinics.CreateAsync(factory);
        var before = await CreateDraftAsync(clinic.Admin, unitPrice: 100m);

        var byReceptionist = await clinic.Receptionist.Http.PutAsJsonAsync("/api/billing/settings", Settings("EUR"), SchedulingApi.Json);
        var invalid = await clinic.Admin.Http.PutAsJsonAsync("/api/billing/settings", Settings("XYZ"), SchedulingApi.Json);
        var saved = await SchedulingApi.ReadAsync<BillingSettings>(
            await clinic.Admin.Http.PutAsJsonAsync("/api/billing/settings", Settings("EUR"), SchedulingApi.Json));
        var after = await CreateDraftAsync(clinic.Admin, unitPrice: 100m);

        Assert.Equal(HttpStatusCode.Forbidden, byReceptionist.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("EUR", saved.Currency);
        Assert.Equal("USD", (await GetAsync(clinic.Admin, before.Id)).Currency);
        Assert.Equal("EUR", after.Currency);
    }

    // ---- Invoices ----

    [Fact]
    public async Task FromACompletedAppointment_OneLineAtTheServicePrice_AndOnlyOnce()
    {
        var clinic = await TestClinics.CreateAsync(factory);
        var appointment = await CompletedAppointmentAsync(clinic.Admin);
        var notCompleted = await BookedAppointmentAsync(clinic.Admin);

        var invoice = await SchedulingApi.ReadAsync<Invoice>(
            await clinic.Receptionist.Http.PostAsync($"/api/invoices/from-appointment/{appointment}", null));
        var again = await clinic.Receptionist.Http.PostAsync($"/api/invoices/from-appointment/{appointment}", null);
        var early = await clinic.Receptionist.Http.PostAsync($"/api/invoices/from-appointment/{notCompleted}", null);

        Assert.Equal("Draft", invoice.Status);
        Assert.Null(invoice.Number);
        Assert.Equal(appointment, invoice.AppointmentId);
        Assert.Equal(80m, Assert.Single(invoice.Lines).UnitPrice);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
    }

    [Fact]
    public async Task Lifecycle_DraftIssuePayInPartsThenPaid()
    {
        var clinic = await TestClinics.CreateAsync(factory);
        var api = clinic.Receptionist;
        var draft = await CreateDraftAsync(api, unitPrice: 100m);

        var edited = await SchedulingApi.ReadAsync<Invoice>(await api.Http.PutAsJsonAsync($"/api/invoices/{draft.Id}", new
        {
            lines = new[] { Line("Exam", 1, 60m), Line("Cleaning", 1, 140m) },
            discountAmount = 0m, taxRatePercent = 0m, insuranceCoveragePercent = 50m
        }, SchedulingApi.Json));
        var issued = await IssueAsync(api, draft.Id);
        var editIssued = await api.Http.PutAsJsonAsync($"/api/invoices/{draft.Id}",
            new { lines = new[] { Line("X", 1, 1m) }, discountAmount = 0m, taxRatePercent = 0m }, SchedulingApi.Json);
        var partial = await PayAsync(api, draft.Id, 50m);
        var tooMuch = await api.Http.PostAsJsonAsync($"/api/invoices/{draft.Id}/payments", new { amount = 151m, method = "Cash" }, SchedulingApi.Json);
        var paid = await PayAsync(api, draft.Id, 150m, "Check");
        var afterPaid = await api.Http.PostAsJsonAsync($"/api/invoices/{draft.Id}/payments", new { amount = 1m, method = "Cash" }, SchedulingApi.Json);

        Assert.Equal(200m, edited.Total);
        Assert.Equal(100m, edited.InsuranceEstimate);
        Assert.Equal(100m, edited.PatientResponsibility);
        Assert.Equal("Issued", issued.Status);
        Assert.Equal("INV-000001", issued.Number);
        Assert.Equal(issued.IssueDate!.Value.AddDays(30), issued.DueDate);
        Assert.Equal(HttpStatusCode.Conflict, editIssued.StatusCode);
        Assert.Equal("PartiallyPaid", partial.Status);
        Assert.Equal(150m, partial.Balance);
        Assert.Equal(HttpStatusCode.BadRequest, tooMuch.StatusCode);
        Assert.Equal("Paid", paid.Status);
        Assert.Equal(0m, paid.Balance);
        Assert.Equal(HttpStatusCode.Conflict, afterPaid.StatusCode);
    }

    [Fact]
    public async Task ConcurrentPayments_NeverOverpay()
    {
        var clinic = await TestClinics.CreateAsync(factory);
        var draft = await CreateDraftAsync(clinic.Admin, unitPrice: 100m);
        await IssueAsync(clinic.Admin, draft.Id);

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            clinic.Receptionist.Http.PostAsJsonAsync($"/api/invoices/{draft.Id}/payments", new { amount = 30m, method = "Cash" }, SchedulingApi.Json)));
        var invoice = await GetAsync(clinic.Admin, draft.Id);

        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(90m, invoice.AmountPaid);
        Assert.Equal(10m, invoice.Balance);
    }

    [Fact]
    public async Task ConcurrentIssuing_GivesDistinctConsecutiveNumbers()
    {
        var clinic = await TestClinics.CreateAsync(factory);
        var drafts = new List<Invoice>();
        for (var i = 0; i < 8; i++) drafts.Add(await CreateDraftAsync(clinic.Admin, unitPrice: 10m));

        var issued = await Task.WhenAll(drafts.Select(d => IssueAsync(clinic.Receptionist, d.Id)));

        Assert.Equal(
            Enumerable.Range(1, 8).Select(n => $"INV-{n:D6}"),
            issued.Select(x => x.Number!).OrderBy(x => x));
    }

    [Fact]
    public async Task RefundsAndVoids_AdminsOnly_AndVoidNeedsNothingPaid()
    {
        var clinic = await TestClinics.CreateAsync(factory);
        var draft = await CreateDraftAsync(clinic.Admin, unitPrice: 100m);
        await IssueAsync(clinic.Admin, draft.Id);
        var paid = await PayAsync(clinic.Receptionist, draft.Id, 100m, "Card");
        var paymentId = paid.Payments.Single().Id;
        var refundUrl = $"/api/invoices/{draft.Id}/payments/{paymentId}/refunds";

        var byReceptionist = await clinic.Receptionist.Http.PostAsJsonAsync(refundUrl, new { amount = 10m, reason = "x" }, SchedulingApi.Json);
        var tooBig = await clinic.Admin.Http.PostAsJsonAsync(refundUrl, new { amount = 101m, reason = "x" }, SchedulingApi.Json);
        var partRefund = await SchedulingApi.ReadAsync<Invoice>(
            await clinic.Admin.Http.PostAsJsonAsync(refundUrl, new { amount = 20m, reason = "Overcharged" }, SchedulingApi.Json));
        var voidWithPayments = await clinic.Admin.Http.PostAsJsonAsync($"/api/invoices/{draft.Id}/void", new { reason = "Mistake" }, SchedulingApi.Json);
        await SchedulingApi.ReadAsync<Invoice>(
            await clinic.Admin.Http.PostAsJsonAsync(refundUrl, new { amount = 80m, reason = "Cancelled treatment" }, SchedulingApi.Json));
        var voidByReceptionist = await clinic.Receptionist.Http.PostAsJsonAsync($"/api/invoices/{draft.Id}/void", new { reason = "Mistake" }, SchedulingApi.Json);
        var voided = await SchedulingApi.ReadAsync<Invoice>(
            await clinic.Admin.Http.PostAsJsonAsync($"/api/invoices/{draft.Id}/void", new { reason = "Billed in error" }, SchedulingApi.Json));

        Assert.Equal(HttpStatusCode.Forbidden, byReceptionist.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooBig.StatusCode);
        Assert.Equal("PartiallyPaid", partRefund.Status);
        Assert.Equal(20m, partRefund.Balance);
        Assert.Equal(HttpStatusCode.Conflict, voidWithPayments.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, voidByReceptionist.StatusCode);
        Assert.Equal("Void", voided.Status);
        Assert.Equal("Billed in error", voided.VoidReason);
    }

    [Fact]
    public async Task Drafts_CanBeDeleted_IssuedInvoicesCant()
    {
        var clinic = await TestClinics.CreateAsync(factory);
        var draft = await CreateDraftAsync(clinic.Receptionist, unitPrice: 10m);
        var other = await CreateDraftAsync(clinic.Receptionist, unitPrice: 10m);
        await IssueAsync(clinic.Receptionist, other.Id);

        Assert.Equal(HttpStatusCode.OK, (await clinic.Receptionist.Http.DeleteAsync($"/api/invoices/{draft.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clinic.Receptionist.Http.GetAsync($"/api/invoices/{draft.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await clinic.Receptionist.Http.DeleteAsync($"/api/invoices/{other.Id}")).StatusCode);
    }

    [Fact]
    public async Task List_FiltersOutstandingAndTotalsByCurrency()
    {
        var clinic = await TestClinics.CreateAsync(factory);
        var open = await CreateDraftAsync(clinic.Admin, unitPrice: 70m);
        await IssueAsync(clinic.Admin, open.Id);
        var settled = await CreateDraftAsync(clinic.Admin, unitPrice: 30m);
        await IssueAsync(clinic.Admin, settled.Id);
        await PayAsync(clinic.Admin, settled.Id, 30m);
        await CreateDraftAsync(clinic.Admin, unitPrice: 5m);

        var all = await ListAsync(clinic.Admin, "");
        var outstanding = await ListAsync(clinic.Admin, "?outstandingOnly=true");
        var byNumber = await ListAsync(clinic.Admin, "?search=INV-000001");

        Assert.Equal(3, all.TotalCount);
        Assert.Equal(open.Id, Assert.Single(outstanding.Items).Id);
        Assert.Equal(70m, Assert.Single(all.Outstanding).Amount);
        Assert.Single(byNumber.Items);
    }

    [Fact]
    public async Task List_FindsTheInvoiceForAnAppointment()
    {
        var clinic = await TestClinics.CreateAsync(factory);
        var appointment = await CompletedAppointmentAsync(clinic.Admin);
        var invoice = await SchedulingApi.ReadAsync<Invoice>(
            await clinic.Admin.Http.PostAsync($"/api/invoices/from-appointment/{appointment}", null));
        await CreateDraftAsync(clinic.Admin, unitPrice: 5m);

        var found = await ListAsync(clinic.Admin, $"?appointmentId={appointment}");

        Assert.Equal(invoice.Id, Assert.Single(found.Items).Id);
    }

    [Fact]
    public async Task Document_HasTheClinicAndPatientForPrinting()
    {
        var clinic = await TestClinics.CreateAsync(factory, "BD", "Asia/Dhaka");
        var patient = await SchedulingApi.ReadAsync<IdDto>(await clinic.Admin.Http.PostAsJsonAsync("/api/patients", new
        {
            firstName = "Nadia", lastName = "Rahman", phone = "01711-000123",
            addressLine1 = "12 Road 5", city = "Dhaka", postalCode = "1209"
        }, SchedulingApi.Json));
        var draft = await SchedulingApi.ReadAsync<Invoice>(await clinic.Admin.Http.PostAsJsonAsync("/api/invoices",
            new { patientId = patient.Id, lines = new[] { Line("Scaling", 1, 1500m) } }, SchedulingApi.Json));

        var doc = await SchedulingApi.ReadAsync<InvoiceDocument>(await clinic.Receptionist.Http.GetAsync($"/api/invoices/{draft.Id}/document"));

        Assert.StartsWith("Clinic", doc.Clinic.Name);
        Assert.Equal("Nadia Rahman", doc.Patient.Name);
        Assert.Equal("12 Road 5", doc.Patient.AddressLine1);
        Assert.Equal("Dhaka, 1209", doc.Patient.CityLine);
        Assert.Equal("BDT", doc.Invoice.Currency);
        Assert.Equal("Fdi", doc.ToothNumbering);
    }

    [Fact]
    public async Task OtherClinics_CantSeeAnInvoice()
    {
        var mine = await TestClinics.CreateAsync(factory);
        var theirs = await TestClinics.CreateAsync(factory);
        var invoice = await CreateDraftAsync(mine.Admin, unitPrice: 10m);

        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Admin.Http.GetAsync($"/api/invoices/{invoice.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Admin.Http.PostAsync($"/api/invoices/{invoice.Id}/issue", null)).StatusCode);
    }

    [Fact]
    public async Task DashboardRevenue_IsMoneyReceived()
    {
        var clinic = await TestClinics.CreateAsync(factory);
        var draft = await CreateDraftAsync(clinic.Admin, unitPrice: 120m);
        await IssueAsync(clinic.Admin, draft.Id);
        var paid = await PayAsync(clinic.Admin, draft.Id, 120m);
        await clinic.Admin.Http.PostAsJsonAsync($"/api/invoices/{draft.Id}/payments/{paid.Payments[0].Id}/refunds",
            new { amount = 20m, reason = "Goodwill" }, SchedulingApi.Json);

        var dashboard = await SchedulingApi.ReadAsync<SchedulingDashboard>(await clinic.Admin.Http.GetAsync("/api/dashboard/scheduling"));

        Assert.Equal(100m, dashboard.RevenueByDay!.Sum(x => x.Revenue));
    }

    // ---- helpers ----

    private static object Line(string description, int quantity, decimal unitPrice) =>
        new { description, quantity, unitPrice, insuranceEstimate = 0m };

    private static object Settings(string currency) =>
        new { currency, invoicePrefix = "INV", paymentTermsDays = 30, defaultTaxRatePercent = 0m, toothNumbering = "Universal" };

    private static async Task<Invoice> CreateDraftAsync(SchedulingApi api, decimal unitPrice)
    {
        var patientId = await api.CreatePatientAsync();
        return await SchedulingApi.ReadAsync<Invoice>(await api.Http.PostAsJsonAsync("/api/invoices",
            new { patientId, lines = new[] { Line("Consultation", 1, unitPrice) } }, SchedulingApi.Json));
    }

    private static async Task<Invoice> IssueAsync(SchedulingApi api, Guid id) =>
        await SchedulingApi.ReadAsync<Invoice>(await api.Http.PostAsync($"/api/invoices/{id}/issue", null));

    private static async Task<Invoice> PayAsync(SchedulingApi api, Guid id, decimal amount, string method = "Cash") =>
        await SchedulingApi.ReadAsync<Invoice>(await api.Http.PostAsJsonAsync($"/api/invoices/{id}/payments",
            new { amount, method, reference = method == "Check" ? "1042" : null }, SchedulingApi.Json));

    private static async Task<Invoice> GetAsync(SchedulingApi api, Guid id) =>
        await SchedulingApi.ReadAsync<Invoice>(await api.Http.GetAsync($"/api/invoices/{id}"));

    private static async Task<InvoiceList> ListAsync(SchedulingApi api, string query) =>
        await SchedulingApi.ReadAsync<InvoiceList>(await api.Http.GetAsync($"/api/invoices{query}"));

    private static async Task<Guid> BookedAppointmentAsync(SchedulingApi api)
    {
        var doctorId = await api.CreateDoctorAsync();
        var serviceId = await api.CreateServiceAsync();
        var patientId = await api.CreatePatientAsync();
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3);
        var slot = (await api.GetSlotsAsync(doctorId, serviceId, date))[0];
        return (await api.BookOkAsync(doctorId, serviceId, patientId, slot.StartTimeUtc)).Id;
    }

    private static async Task<Guid> CompletedAppointmentAsync(SchedulingApi api)
    {
        var id = await BookedAppointmentAsync(api);
        Assert.Equal(HttpStatusCode.OK, (await api.Http.PostAsJsonAsync($"/api/appointments/{id}/confirm", new { }, SchedulingApi.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.Http.PostAsJsonAsync($"/api/appointments/{id}/complete", new { }, SchedulingApi.Json)).StatusCode);
        return id;
    }
}
