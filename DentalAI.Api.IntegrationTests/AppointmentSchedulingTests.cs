using System.Net;
using DentalAI.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace DentalAI.Api.IntegrationTests;

/// <summary>
/// Booking, rescheduling and cancelling against the real API and SQL Server. Each test makes its own
/// doctor, so tests never compete for the same calendar.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AppointmentSchedulingTests(ApiFactory factory) : IAsyncLifetime
{
    private SchedulingApi _api = null!;
    private Guid _serviceId;
    private Guid _patientId;
    private Guid _doctorId;
    private readonly DateOnly _date = SchedulingApi.FutureClinicDate();

    public async Task InitializeAsync()
    {
        _api = await SchedulingApi.SignInAsync(factory);
        _serviceId = await _api.CreateServiceAsync();
        _patientId = await _api.CreatePatientAsync();
        _doctorId = await _api.CreateDoctorAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ---- Double booking (gap 3) ----

    [Fact]
    public async Task ConcurrentBookingsOfOneSlot_OnlyOneSucceeds()
    {
        var slot = (await _api.GetSlotsAsync(_doctorId, _serviceId, _date))[0];

        var responses = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => _api.BookAsync(_doctorId, _serviceId, _patientId, slot.StartTimeUtc)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.OK),
            r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Single(await DoctorAppointmentsAsync());
    }

    [Fact]
    public async Task ConcurrentOverlappingBookings_OnlyOneSucceeds()
    {
        // 09:00, 09:15 and 09:30-style neighbours: every pair of 30-minute bookings 15 minutes apart overlaps.
        var slots = await _api.GetSlotsAsync(_doctorId, _serviceId, _date);
        var starts = new[] { slots[0].StartTimeUtc, slots[1].StartTimeUtc, slots[0].StartTimeUtc, slots[1].StartTimeUtc };

        var responses = await Task.WhenAll(starts.Select(start => _api.BookAsync(_doctorId, _serviceId, _patientId, start)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
    }

    [Fact]
    public async Task TheLockIsPerDoctor_OtherDoctorsBookTheSameTimeFreely()
    {
        var otherDoctorId = await _api.CreateDoctorAsync();
        var start = (await _api.GetSlotsAsync(_doctorId, _serviceId, _date))[0].StartTimeUtc;

        var responses = await Task.WhenAll(
            _api.BookAsync(_doctorId, _serviceId, _patientId, start),
            _api.BookAsync(otherDoctorId, _serviceId, _patientId, start));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    // ---- Reschedule ----

    [Fact]
    public async Task Reschedule_OverlappingItsOwnOldTime_Succeeds()
    {
        var slots = await _api.GetSlotsAsync(_doctorId, _serviceId, _date);
        var appointment = await _api.BookOkAsync(_doctorId, _serviceId, _patientId, slots[0].StartTimeUtc);
        var newStart = slots[0].StartTimeUtc.AddMinutes(15);

        var moved = await SchedulingApi.ReadAsync<AppointmentDto>(await _api.RescheduleAsync(appointment.Id, newStart));

        Assert.Equal(newStart, moved.StartTime);
        Assert.Equal(newStart.AddMinutes(30), moved.EndTime);
        Assert.Equal("Pending", moved.Status);
    }

    [Fact]
    public async Task Reschedule_OntoAnotherAppointment_Returns409()
    {
        var slots = await _api.GetSlotsAsync(_doctorId, _serviceId, _date);
        var first = await _api.BookOkAsync(_doctorId, _serviceId, _patientId, slots[0].StartTimeUtc);
        var second = await _api.BookOkAsync(_doctorId, _serviceId, _patientId, slots[0].StartTimeUtc.AddHours(2));

        var response = await _api.RescheduleAsync(second.Id, first.StartTime.AddMinutes(15));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Reschedule_IntoThePast_Returns400()
    {
        var slot = (await _api.GetSlotsAsync(_doctorId, _serviceId, _date))[0];
        var appointment = await _api.BookOkAsync(_doctorId, _serviceId, _patientId, slot.StartTimeUtc);

        var response = await _api.RescheduleAsync(appointment.Id, DateTime.UtcNow.AddDays(-1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reschedule_CancelledAppointment_Returns409()
    {
        var slots = await _api.GetSlotsAsync(_doctorId, _serviceId, _date);
        var appointment = await _api.BookOkAsync(_doctorId, _serviceId, _patientId, slots[0].StartTimeUtc);
        await _api.CancelOkAsync(appointment.Id);

        var response = await _api.RescheduleAsync(appointment.Id, slots[4].StartTimeUtc);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ConcurrentReschedulesIntoOneSlot_OnlyOneSucceeds()
    {
        var slots = await _api.GetSlotsAsync(_doctorId, _serviceId, _date);
        var first = await _api.BookOkAsync(_doctorId, _serviceId, _patientId, slots[0].StartTimeUtc);
        var second = await _api.BookOkAsync(_doctorId, _serviceId, _patientId, slots[0].StartTimeUtc.AddHours(1));
        var target = slots[0].StartTimeUtc.AddHours(4);

        var responses = await Task.WhenAll(_api.RescheduleAsync(first.Id, target), _api.RescheduleAsync(second.Id, target));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task AvailableSlots_ExcludingAnAppointment_FreesItsOwnTime()
    {
        var start = (await _api.GetSlotsAsync(_doctorId, _serviceId, _date))[0].StartTimeUtc;
        var appointment = await _api.BookOkAsync(_doctorId, _serviceId, _patientId, start);

        var normal = await _api.GetSlotsAsync(_doctorId, _serviceId, _date);
        var excluding = await _api.GetSlotsAsync(_doctorId, _serviceId, _date, appointment.Id);

        Assert.DoesNotContain(normal, s => s.StartTimeUtc == start);
        Assert.Contains(excluding, s => s.StartTimeUtc == start);
    }

    // ---- Cancel ----

    [Fact]
    public async Task Cancel_FreesTheSlotForAnotherBooking()
    {
        var start = (await _api.GetSlotsAsync(_doctorId, _serviceId, _date))[0].StartTimeUtc;
        var appointment = await _api.BookOkAsync(_doctorId, _serviceId, _patientId, start);

        var cancelled = await _api.CancelOkAsync(appointment.Id);
        var rebooked = await _api.BookAsync(_doctorId, _serviceId, _patientId, start);

        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Equal(HttpStatusCode.OK, rebooked.StatusCode);
    }

    // ---- Clinic time zone on read models ----

    [Fact]
    public async Task AppointmentAndCalendarData_CarryTheClinicTimeZone()
    {
        var start = (await _api.GetSlotsAsync(_doctorId, _serviceId, _date))[0].StartTimeUtc;
        var created = await _api.BookOkAsync(_doctorId, _serviceId, _patientId, start);

        var listed = (await DoctorAppointmentsAsync()).Single();
        var calendar = await _api.GetCalendarDayAsync(_doctorId, _date);

        Assert.Equal(SchedulingApi.ClinicTimeZone, created.ClinicTimeZone);
        Assert.Equal(SchedulingApi.ClinicTimeZone, listed.ClinicTimeZone);
        Assert.Equal(SchedulingApi.ClinicTimeZone, Assert.Single(calendar.Appointments).ClinicTimeZone);
        Assert.Equal(DateTimeKind.Utc, listed.StartTime.Kind);
    }

    private async Task<List<AppointmentDto>> DoctorAppointmentsAsync() =>
        (await _api.ListAsync()).Where(a => a.DoctorId == _doctorId).ToList();
}
