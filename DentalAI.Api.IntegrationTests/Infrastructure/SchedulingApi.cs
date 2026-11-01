using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DentalAI.Api.IntegrationTests.Infrastructure;

public sealed record Envelope<T>(bool Success, string? Message, T? Data, string[]? Errors);
public sealed record IdDto(Guid Id);
public sealed record SlotDto(DateTime StartTimeUtc, DateTime EndTimeUtc, string? ClinicTimeZone = null);
public sealed record AppointmentDto(
    Guid Id, Guid DoctorId, Guid PatientId, Guid ServiceId, DateTime StartTime, DateTime EndTime,
    string Status, string? ClinicTimeZone);
public sealed record CalendarAppointmentDto(Guid Id, DateTime StartTimeUtc, string Status, string? ClinicTimeZone);
public sealed record CalendarDto(List<CalendarAppointmentDto> Appointments);
public sealed record AuthDto(string AccessToken);

/// <summary>Typed calls to the scheduling endpoints, signed in as the seeded clinic admin of the demo clinic.</summary>
public sealed class SchedulingApi
{
    /// <summary>The seeded "Demo Dental Clinic" and its zone (see ApplicationDbContextSeeder).</summary>
    public static readonly Guid ClinicId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public const string ClinicTimeZone = "Asia/Dhaka";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _http;

    private SchedulingApi(HttpClient http) => _http = http;

    public HttpClient Http => _http;

    public static Task<SchedulingApi> SignInAsync(ApiFactory factory) =>
        SignInAsync(factory, "clinicadmin@dentalai.local", "ClinicAdmin123!");

    public static async Task<SchedulingApi> SignInAsync(ApiFactory factory, string email, string password)
    {
        var http = factory.CreateClient();
        var login = await http.PostAsJsonAsync("/api/auth/login",
            new { email, password });
        var auth = await ReadAsync<AuthDto>(login);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return new SchedulingApi(http);
    }

    /// <summary>A fresh doctor working every day 06:00–20:00 clinic time, so tests never share a calendar.</summary>
    public async Task<Guid> CreateDoctorAsync()
    {
        var doctor = await ReadAsync<IdDto>(await _http.PostAsJsonAsync("/api/doctors", new
        {
            clinicId = ClinicId,
            firstName = "Test",
            lastName = $"Doctor {Guid.NewGuid():N}"[..20],
            specialty = "General",
            phone = "+1 555 010 0100",
            email = $"doctor.{Guid.NewGuid():N}@test.local"
        }, Json));

        var days = Enum.GetValues<DayOfWeek>()
            .Select(day => new { dayOfWeek = day, startTime = "06:00", endTime = "20:00", isWorking = true });
        var saved = await _http.PutAsJsonAsync($"/api/doctorSchedules/{doctor.Id}",
            new { schedules = days, breaks = Array.Empty<object>(), leaves = Array.Empty<object>() }, Json);
        await ReadAsync<JsonElement>(saved);
        return doctor.Id;
    }

    /// <summary>An active 30-minute service in the seeded clinic.</summary>
    public async Task<Guid> CreateServiceAsync() =>
        (await ReadAsync<IdDto>(await _http.PostAsJsonAsync("/api/services", new
        {
            name = $"Cleaning {Guid.NewGuid():N}"[..20],
            price = 80m,
            durationMinutes = 30,
            isActive = true
        }, Json))).Id;

    public async Task<Guid> CreatePatientAsync() =>
        (await ReadAsync<IdDto>(await _http.PostAsJsonAsync("/api/patients", new
        {
            clinicId = ClinicId,
            firstName = "Test",
            lastName = "Patient",
            phone = "+1 555 010 0199"
        }, Json))).Id;

    /// <summary>A clinic-local date far enough ahead that every slot is in the future.</summary>
    public static DateOnly FutureClinicDate(int daysAhead = 3)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(ClinicTimeZone);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone)).AddDays(daysAhead);
    }

    public async Task<List<SlotDto>> GetSlotsAsync(Guid doctorId, Guid serviceId, DateOnly date, Guid? excludeAppointmentId = null)
    {
        var url = $"/api/appointments/available-slots?doctorId={doctorId}&serviceId={serviceId}&date={date:yyyy-MM-dd}";
        if (excludeAppointmentId.HasValue) url += $"&excludeAppointmentId={excludeAppointmentId}";
        return await ReadAsync<List<SlotDto>>(await _http.GetAsync(url));
    }

    public Task<HttpResponseMessage> BookAsync(Guid doctorId, Guid serviceId, Guid patientId, DateTime startUtc) =>
        _http.PostAsJsonAsync("/api/appointments", new { doctorId, serviceId, patientId, startTime = startUtc }, Json);

    public async Task<AppointmentDto> BookOkAsync(Guid doctorId, Guid serviceId, Guid patientId, DateTime startUtc) =>
        await ReadAsync<AppointmentDto>(await BookAsync(doctorId, serviceId, patientId, startUtc));

    public Task<HttpResponseMessage> RescheduleAsync(Guid appointmentId, DateTime startUtc) =>
        _http.PostAsJsonAsync($"/api/appointments/{appointmentId}/reschedule", new { startTime = startUtc }, Json);

    public async Task<AppointmentDto> CancelOkAsync(Guid appointmentId) =>
        await ReadAsync<AppointmentDto>(await _http.PostAsJsonAsync($"/api/appointments/{appointmentId}/cancel", new { }, Json));

    public async Task<List<AppointmentDto>> ListAsync() =>
        await ReadAsync<List<AppointmentDto>>(await _http.GetAsync("/api/appointments"));

    public async Task<CalendarDto> GetCalendarDayAsync(Guid doctorId, DateOnly date) =>
        await ReadAsync<CalendarDto>(await _http.GetAsync($"/api/calendar/day?date={date:yyyy-MM-dd}&doctorId={doctorId}"));

    /// <summary>Reads a successful ApiResponse envelope, failing with the body otherwise.</summary>
    public static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new HttpRequestException($"{(int)response.StatusCode} {response.RequestMessage?.RequestUri}: {body}");
        }

        var envelope = JsonSerializer.Deserialize<Envelope<T>>(body, Json)
            ?? throw new InvalidOperationException($"Empty response from {response.RequestMessage?.RequestUri}");
        return envelope.Data!;
    }
}
