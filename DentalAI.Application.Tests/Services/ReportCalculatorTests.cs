using DentalAI.Application.Common.Interfaces.Persistence;
using DentalAI.Application.Common.Reports;
using DentalAI.Application.DTOs.Reports;
using DentalAI.Application.Services;
using DentalAI.Domain.Enums;
using Xunit;

namespace DentalAI.Application.Tests.Services;

public sealed class ReportCalculatorTests
{
    private static readonly TimeZoneInfo NewYork = SchedulingEngine.ResolveTimeZone("America/New_York");
    private static readonly Guid DrA = Guid.NewGuid();
    private static readonly Guid DrB = Guid.NewGuid();
    private static readonly DateOnly July1 = new(2026, 7, 1);
    private static readonly DateOnly July31 = new(2026, 7, 31);

    [Theory]
    [InlineData(1, ReportGranularity.Day)]
    [InlineData(31, ReportGranularity.Day)]
    [InlineData(32, ReportGranularity.Week)]
    [InlineData(92, ReportGranularity.Week)]
    [InlineData(93, ReportGranularity.Month)]
    [InlineData(366, ReportGranularity.Month)]
    public void Granularity_FollowsTheLengthOfTheRange(int days, ReportGranularity expected) =>
        Assert.Equal(expected, ReportCalculator.GranularityFor(July1, July1.AddDays(days - 1)));

    [Fact]
    public void PreviousRange_IsTheSameLength_JustBefore()
    {
        Assert.Equal((new DateOnly(2026, 5, 31), new DateOnly(2026, 6, 30)), ReportCalculator.PreviousRange(July1, July31));
    }

    [Fact]
    public void UtcWindow_UsesLocalMidnights_AcrossDst()
    {
        // US clocks spring forward on 8 March 2026: the day starts at UTC-5, the next day at UTC-4.
        var (from, to) = ReportCalculator.UtcWindow(new DateOnly(2026, 3, 8), new DateOnly(2026, 3, 8), NewYork);

        Assert.Equal(new DateTime(2026, 3, 8, 5, 0, 0, DateTimeKind.Utc), from);
        Assert.Equal(new DateTime(2026, 3, 9, 4, 0, 0, DateTimeKind.Utc), to);
    }

    [Fact]
    public void Kpis_CountByClinicDate_AndOnlyTheClinicsCurrency()
    {
        var patient = Guid.NewGuid();
        var report = ReportCalculator.Build(Input(
            appointments:
            [
                Visit(At(July1, 10), AppointmentStatus.Completed, DrA, patient),
                Visit(At(July1, 11), AppointmentStatus.Completed, DrA, patient),        // same patient: seen once
                Visit(At(new DateOnly(2026, 7, 2), 9), AppointmentStatus.NoShow, DrB),
                Visit(At(new DateOnly(2026, 7, 3), 9), AppointmentStatus.Cancelled, DrB),
                Visit(new DateTime(2026, 7, 1, 2, 0, 0, DateTimeKind.Utc), AppointmentStatus.Completed, DrA) // June 30, 10 PM in NY
            ],
            invoices:
            [
                Invoice(July1, 200m, discount: 20m, DrA),
                Invoice(new DateOnly(2026, 7, 2), 100m, discount: 0m, null),
                Invoice(July1, 999m, discount: 0m, DrA, currency: "CAD")
            ],
            money:
            [
                Money(At(July1, 12), 150m, PaymentMethod.Card, DrA),
                Money(At(new DateOnly(2026, 7, 5), 12), -30m, PaymentMethod.Card, DrA, refund: true),
                Money(At(July1, 12), 500m, PaymentMethod.Cash, DrA, currency: "CAD")
            ],
            added: [At(July1, 9), new DateTime(2026, 7, 1, 3, 0, 0, DateTimeKind.Utc)]));

        var k = report.Current;
        Assert.Equal(280m, k.Production);           // (200 − 20) + 100; tax and CAD left out
        Assert.Equal(120m, k.Collections);          // 150 − 30 refunded
        Assert.Equal((4, 2, 1, 1), (k.Booked, k.Completed, k.Cancelled, k.NoShows));
        Assert.Equal(0.3333m, k.NoShowRate);        // 1 ÷ (2 + 1)
        Assert.Equal(0.25m, k.CancellationRate);    // 1 ÷ 4
        Assert.Equal(1, k.PatientsSeen);
        Assert.Equal(1, k.NewPatients);
        Assert.Equal(140m, k.ProductionPerVisit);
        Assert.Equal(1, report.OtherCurrencyInvoices);

        Assert.Equal(1, report.Previous.Completed); // the June 30 visit
        Assert.Equal(1, report.Previous.NewPatients);
    }

    [Fact]
    public void Periods_AreDays_ForAMonth_AndWeeksAreClampedToTheRange()
    {
        var month = ReportCalculator.Build(Input());
        Assert.Equal(31, month.Periods.Count);

        var quarter = ReportCalculator.Build(Input(from: July1, to: new DateOnly(2026, 9, 30)));  // 1 July 2026 is a Wednesday
        Assert.Equal(ReportGranularity.Week, quarter.Granularity);
        Assert.Equal((July1, new DateOnly(2026, 7, 4)), (quarter.Periods[0].Start, quarter.Periods[0].End));
        Assert.Equal(new DateOnly(2026, 7, 5), quarter.Periods[1].Start);                         // Sunday
        Assert.Equal(new DateOnly(2026, 9, 30), quarter.Periods[^1].End);
    }

    [Fact]
    public void Doctors_GetTheirVisitsAndMoney_AndUnlinkedMoneyHasItsOwnRow()
    {
        var report = ReportCalculator.Build(Input(
            appointments: [Visit(At(July1, 10), AppointmentStatus.Completed, DrA), Visit(At(July1, 11), AppointmentStatus.NoShow, DrB)],
            invoices: [Invoice(July1, 300m, 0m, DrA), Invoice(July1, 50m, 0m, null)],
            money: [Money(At(July1, 12), 300m, PaymentMethod.Cash, DrA)]));

        var a = report.Doctors.Single(d => d.DoctorId == DrA);
        var none = report.Doctors.Single(d => d.DoctorId is null);
        Assert.Equal((1, 300m, 300m), (a.Completed, a.Production, a.Collections));
        Assert.Equal(1, report.Doctors.Single(d => d.DoctorId == DrB).NoShows);
        Assert.Equal((ReportCalculator.NoDoctor, 50m), (none.Name, none.Production));
        Assert.Null(report.Doctors[^1].DoctorId);   // listed last
    }

    [Fact]
    public void Services_GroupLines_AndPaymentMethodsNetRefunds()
    {
        var cleaning = Guid.NewGuid();
        var report = ReportCalculator.Build(Input(
            lines:
            [
                new ReportLineRow(July1, "USD", cleaning, "Cleaning", 1, 80m),
                new ReportLineRow(July1, "USD", cleaning, "Cleaning", 2, 160m),
                new ReportLineRow(July1, "USD", null, "X-ray ", 1, 40m),
                new ReportLineRow(July1, "USD", null, "x-ray", 1, 40m)
            ],
            money:
            [
                Money(At(July1, 12), 100m, PaymentMethod.Card, DrA),
                Money(At(July1, 13), 50m, PaymentMethod.Card, DrA),
                Money(At(July1, 14), -20m, PaymentMethod.Card, DrA, refund: true)
            ]));

        Assert.Equal([("Cleaning", 3, 240m), ("X-ray", 2, 80m)], report.Services.Select(s => (s.Name, s.Quantity, s.Amount)));
        var card = Assert.Single(report.PaymentMethods);
        Assert.Equal((2, 130m), (card.Payments, card.Amount));
    }

    [Fact]
    public void WithoutMoney_KeepsCounts_AndDropsEveryMoneyFigure()
    {
        var report = ReportCalculator.WithoutMoney(ReportCalculator.Build(Input(
            appointments: [Visit(At(July1, 10), AppointmentStatus.Completed, DrA), Visit(At(July1, 11), AppointmentStatus.Completed, DrB)],
            invoices: [Invoice(July1, 300m, 0m, DrA), Invoice(July1, 50m, 0m, null)],
            lines: [new ReportLineRow(July1, "USD", null, "Crown", 1, 900m), new ReportLineRow(July1, "USD", null, "Cleaning", 3, 240m)],
            money: [Money(At(July1, 12), 300m, PaymentMethod.Cash, DrA)])));

        Assert.False(report.IncludesMoney);
        Assert.Equal((null, null, null, 2), (report.Current.Production, report.Current.Collections, report.Current.ProductionPerVisit, report.Current.Completed));
        Assert.Equal((null, null), (report.Previous.Production, report.Previous.Collections));
        Assert.All(report.Periods, p => Assert.Equal((null, null), (p.Production, p.Collections)));
        // No "no appointment linked" row: it only ever holds money.
        Assert.Equal(new[] { DrA, DrB }.Order(), report.Doctors.Select(d => d.DoctorId!.Value).Order());
        Assert.All(report.Doctors, d => Assert.Equal((null, null), (d.Production, d.Collections)));
        // Ordered by quantity, not by what they earned.
        Assert.Equal([("Cleaning", 3, (decimal?)null), ("Crown", 1, null)], report.Services.Select(s => (s.Name, s.Quantity, s.Amount)));
        Assert.Empty(report.PaymentMethods);
    }

    [Fact]
    public void Receivables_AreBucketedByDaysPastDue()
    {
        var asOf = new DateOnly(2026, 7, 31);
        var ava = Guid.NewGuid();
        var ben = Guid.NewGuid();
        ReceivableRow Owed(Guid patient, string name, int daysPastDue, decimal balance, string currency = "USD") =>
            new(Guid.NewGuid(), "INV", patient, name, asOf.AddDays(-daysPastDue - 30), asOf.AddDays(-daysPastDue), balance, currency);

        var report = ReportCalculator.Receivables(
        [
            Owed(ava, "Ava", -5, 10m),   // not yet due
            Owed(ava, "Ava", 0, 20m),    // due today: still current
            Owed(ava, "Ava", 30, 30m),
            Owed(ben, "Ben", 31, 40m),
            Owed(ben, "Ben", 90, 50m),
            Owed(ben, "Ben", 91, 60m),
            Owed(ben, "Ben", 120, 70m, "CAD")
        ], asOf, "USD");

        Assert.Equal(new AgingBucketsDto(30m, 30m, 40m, 50m, 60m), report.Totals);
        Assert.Equal(210m, report.Totals.Total);
        Assert.Equal(["Ben", "Ava"], report.Patients.Select(p => p.PatientName)); // most overdue first
        Assert.Equal(3, report.Patients[0].OpenInvoices);
        Assert.Equal(1, report.OtherCurrencyInvoices);
    }

    // ---- helpers ----

    private static DateTime At(DateOnly date, int hour) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(new TimeOnly(hour, 0)), NewYork);

    private static ReportAppointmentRow Visit(DateTime utc, AppointmentStatus status, Guid doctor, Guid? patient = null) =>
        new(Guid.NewGuid(), utc, status, doctor, doctor == DrA ? "Ana Lopez" : "Ben Ray", patient ?? Guid.NewGuid());

    private static ReportInvoiceRow Invoice(DateOnly issued, decimal subtotal, decimal discount, Guid? doctor, string currency = "USD") =>
        new(Guid.NewGuid(), issued, currency, subtotal, discount, doctor, doctor is null ? null : "Ana Lopez");

    private static ReportMoneyRow Money(DateTime utc, decimal amount, PaymentMethod method, Guid? doctor, bool refund = false, string currency = "USD") =>
        new(utc, amount, refund, method, currency, doctor, doctor is null ? null : "Ana Lopez");

    private static ReportInput Input(
        DateOnly? from = null, DateOnly? to = null,
        ReportAppointmentRow[]? appointments = null, ReportInvoiceRow[]? invoices = null, ReportLineRow[]? lines = null,
        ReportMoneyRow[]? money = null, DateTime[]? added = null) =>
        new(from ?? July1, to ?? July31, NewYork, "USD", appointments ?? [], invoices ?? [], lines ?? [], money ?? [], added ?? []);
}
