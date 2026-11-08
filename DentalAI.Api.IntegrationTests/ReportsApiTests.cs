using System.Net;
using System.Net.Http.Json;
using DentalAI.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace DentalAI.Api.IntegrationTests;

public sealed record ReportKpis(decimal? Production, decimal? Collections, int Booked, int Completed, int Cancelled, int NoShows,
    int PatientsSeen, int NewPatients);
public sealed record DoctorRow(Guid? DoctorId, string Name, int Completed, decimal? Production, decimal? Collections);
public sealed record ServiceReportRow(Guid? ServiceId, string Name, int Quantity, decimal? Amount);
public sealed record MethodRow(string Method, int Payments, decimal Amount);
public sealed record PracticeReport(DateOnly From, DateOnly To, string Currency, string Granularity, ReportKpis Current,
    ReportKpis Previous, List<object> Periods, List<DoctorRow> Doctors, List<ServiceReportRow> Services, List<MethodRow> PaymentMethods, bool IncludesMoney);
public sealed record AgingBuckets(decimal Current, decimal Days1To30, decimal Total);
public sealed record ReceivablePatient(Guid PatientId, string PatientName, int OpenInvoices, AgingBuckets Buckets);
public sealed record Receivables(string Currency, AgingBuckets Totals, List<ReceivablePatient> Patients);

/// <summary>Phase 4: practice reports and receivables, end to end through booking, invoicing and payment.</summary>
[Collection(ApiCollection.Name)]
public sealed class ReportsApiTests(ApiFactory factory)
{
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, NewYork));

    [Fact]
    public async Task PracticeReport_CountsAVisitItsInvoiceAndPayment()
    {
        var clinic = await TestClinics.CreateAsync(factory);
        var (doctorId, patientId) = await CompletedVisitPaidAsync(clinic.Admin, pay: 50m);

        var report = await PracticeAsync(clinic.Admin, Today.AddDays(-1), Today.AddDays(6));

        Assert.Equal("USD", report.Currency);
        Assert.Equal("Day", report.Granularity);
        Assert.Equal(8, report.Periods.Count);
        Assert.Equal((80m, 50m, 1, 1, 1, 1), (report.Current.Production, report.Current.Collections, report.Current.Booked,
            report.Current.Completed, report.Current.PatientsSeen, report.Current.NewPatients));
        var doctor = Assert.Single(report.Doctors);
        Assert.Equal((doctorId, 1, 80m, 50m), (doctor.DoctorId, doctor.Completed, doctor.Production, doctor.Collections));
        Assert.Equal((1, 80m), (Assert.Single(report.Services).Quantity, report.Services[0].Amount));
        Assert.Equal(("Cash", 1, 50m), (report.PaymentMethods[0].Method, report.PaymentMethods[0].Payments, report.PaymentMethods[0].Amount));

        var owed = await SchedulingApi.ReadAsync<Receivables>(await clinic.Receptionist.Http.GetAsync("/api/reports/receivables"));
        Assert.Equal((30m, 30m), (owed.Totals.Current, owed.Totals.Total)); // due in 30 days: not overdue yet
        Assert.Equal(patientId, Assert.Single(owed.Patients).PatientId);
    }

    [Fact]
    public async Task Receptionists_GetCountsButNoPracticeMoney()
    {
        var clinic = await TestClinics.CreateAsync(factory);
        var (doctorId, _) = await CompletedVisitPaidAsync(clinic.Admin, pay: 50m);

        var report = await PracticeAsync(clinic.Receptionist, Today.AddDays(-1), Today.AddDays(6));
        var dashboard = await SchedulingApi.ReadAsync<SchedulingDashboard>(await clinic.Receptionist.Http.GetAsync("/api/dashboard/scheduling"));

        Assert.False(report.IncludesMoney);
        Assert.Equal((null, null, 1, 1), (report.Current.Production, report.Current.Collections, report.Current.Completed, report.Current.NewPatients));
        var doctor = Assert.Single(report.Doctors);
        Assert.Equal((doctorId, 1, null, null), (doctor.DoctorId, doctor.Completed, doctor.Production, doctor.Collections));
        Assert.Equal((1, null), (Assert.Single(report.Services).Quantity, report.Services[0].Amount));
        Assert.Empty(report.PaymentMethods);
        Assert.Null(dashboard.RevenueByDay);
        // Receivables stay open to them: the front desk takes payments and chases balances.
        Assert.Equal(HttpStatusCode.OK, (await clinic.Receptionist.Http.GetAsync("/api/reports/receivables")).StatusCode);
    }

    [Fact]
    public async Task Reports_DefaultToThisMonth()
    {
        var clinic = await TestClinics.CreateAsync(factory);

        var report = await SchedulingApi.ReadAsync<PracticeReport>(await clinic.Admin.Http.GetAsync("/api/reports/practice"));

        Assert.Equal((new DateOnly(Today.Year, Today.Month, 1), Today), (report.From, report.To));
    }

    [Theory]
    [InlineData(5, 1)]      // backwards
    [InlineData(0, 400)]    // more than 366 days
    public async Task BadRanges_Return400(int fromOffset, int toOffset)
    {
        var clinic = await TestClinics.CreateAsync(factory);

        var response = await clinic.Admin.Http.GetAsync(
            $"/api/reports/practice?from={Today.AddDays(fromOffset):yyyy-MM-dd}&to={Today.AddDays(toOffset):yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reports_ArePerClinic_AndNotForPlatformAdmins()
    {
        var mine = await TestClinics.CreateAsync(factory);
        var theirs = await TestClinics.CreateAsync(factory);
        await CompletedVisitPaidAsync(mine.Admin, pay: 80m);
        var platform = await SchedulingApi.SignInAsync(factory, "superadmin@dentalai.local", "SuperAdmin123!");

        var other = await PracticeAsync(theirs.Admin, Today.AddDays(-1), Today.AddDays(6));
        var forbidden = await platform.Http.GetAsync("/api/reports/practice");

        Assert.Equal((0m, 0m, 0), (other.Current.Production, other.Current.Collections, other.Current.Booked));
        Assert.Empty(other.Doctors);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    private static async Task<PracticeReport> PracticeAsync(SchedulingApi api, DateOnly from, DateOnly to) =>
        await SchedulingApi.ReadAsync<PracticeReport>(await api.Http.GetAsync($"/api/reports/practice?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}"));

    /// <summary>Books an 80.00 service 3 days ahead, completes it, invoices it, issues the invoice and records a cash payment.</summary>
    private static async Task<(Guid DoctorId, Guid PatientId)> CompletedVisitPaidAsync(SchedulingApi api, decimal pay)
    {
        var doctorId = await api.CreateDoctorAsync();
        var serviceId = await api.CreateServiceAsync();
        var patientId = await api.CreatePatientAsync();
        var slot = (await api.GetSlotsAsync(doctorId, serviceId, Today.AddDays(3)))[0];
        var appointment = await api.BookOkAsync(doctorId, serviceId, patientId, slot.StartTimeUtc);
        Assert.Equal(HttpStatusCode.OK, (await api.Http.PostAsJsonAsync($"/api/appointments/{appointment.Id}/confirm", new { }, SchedulingApi.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.Http.PostAsJsonAsync($"/api/appointments/{appointment.Id}/complete", new { }, SchedulingApi.Json)).StatusCode);

        var draft = await SchedulingApi.ReadAsync<Invoice>(await api.Http.PostAsync($"/api/invoices/from-appointment/{appointment.Id}", null));
        await SchedulingApi.ReadAsync<Invoice>(await api.Http.PostAsync($"/api/invoices/{draft.Id}/issue", null));
        await SchedulingApi.ReadAsync<Invoice>(await api.Http.PostAsJsonAsync($"/api/invoices/{draft.Id}/payments",
            new { amount = pay, method = "Cash" }, SchedulingApi.Json));
        return (doctorId, patientId);
    }
}
