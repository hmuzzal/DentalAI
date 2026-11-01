using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DentalAI.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace DentalAI.Api.IntegrationTests;

public sealed record InsuranceRow(Guid Id, string Priority, string CarrierName, string MemberId, string? GroupNumber,
    string RelationshipToPatient, string SubscriberName, DateOnly? SubscriberDateOfBirth);
public sealed record PatientDetail(Guid Id, string FirstName, string LastName, string Phone, string? Email,
    DateOnly? DateOfBirth, string? AddressLine1, string? City, string? State, string? PostalCode,
    string PreferredContactMethod, bool SmsOptIn, DateTime? SmsOptInAtUtc, DateTime? SmsOptOutAtUtc,
    List<InsuranceRow> Insurances);

/// <summary>Phase 1.5: E.164 phones, address, contact preference, SMS consent and insurance.</summary>
[Collection(ApiCollection.Name)]
public sealed class PatientUsReadinessTests(ApiFactory factory) : IAsyncLifetime
{
    private SchedulingApi _api = null!;

    public async Task InitializeAsync() => _api = await SchedulingApi.SignInAsync(factory);

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Phone_IsStoredAsE164_AndInvalidNumbersAreRejected()
    {
        var created = await CreateAsync(Body(phone: "+1 (212) 555-0142"));
        var invalid = await _api.Http.PostAsJsonAsync("/api/patients", Body(phone: "+1 (555) 010-0100"), SchedulingApi.Json);

        Assert.Equal("+12125550142", created.Phone);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("phone", await invalid.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Phone_WithoutCountryCode_UsesTheClinicCountry()
    {
        // The seeded demo clinic is in Bangladesh, so a local mobile number is read as +880.
        var created = await CreateAsync(Body(phone: "01711-000199"));

        Assert.Equal("+8801711000199", created.Phone);
    }

    [Fact]
    public async Task AddressAndContactPreference_RoundTrip()
    {
        var created = await CreateAsync(Body(extra: new()
        {
            ["addressLine1"] = "350 5th Ave", ["city"] = "New York", ["state"] = "ny", ["postalCode"] = "10118",
            ["email"] = "ava@example.com", ["preferredContactMethod"] = "Email"
        }));
        var fetched = await GetAsync(created.Id);

        Assert.Equal("350 5th Ave", fetched.AddressLine1);
        Assert.Equal("NY", fetched.State);
        Assert.Equal("10118", fetched.PostalCode);
        Assert.Equal("Email", fetched.PreferredContactMethod);
    }

    [Fact]
    public async Task SmsAsPreferredContact_RequiresConsent()
    {
        var response = await _api.Http.PostAsJsonAsync("/api/patients",
            Body(extra: new() { ["preferredContactMethod"] = "Sms", ["smsOptIn"] = false }), SchedulingApi.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("SMS consent", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SmsConsent_RecordsWhenItWasGivenAndWithdrawn()
    {
        var optedIn = await CreateAsync(Body(extra: new() { ["smsOptIn"] = true }));
        var optedOut = await UpdateAsync(optedIn.Id, Body(extra: new() { ["smsOptIn"] = false }));
        var optedInAgain = await UpdateAsync(optedIn.Id, Body(extra: new() { ["smsOptIn"] = true }));

        Assert.True(optedIn.SmsOptIn);
        Assert.NotNull(optedIn.SmsOptInAtUtc);
        Assert.Null(optedIn.SmsOptOutAtUtc);
        Assert.False(optedOut.SmsOptIn);
        Assert.Equal(optedIn.SmsOptInAtUtc, optedOut.SmsOptInAtUtc); // the earlier consent is kept on record
        Assert.NotNull(optedOut.SmsOptOutAtUtc);
        Assert.True(optedInAgain.SmsOptIn);
        Assert.Null(optedInAgain.SmsOptOutAtUtc);
    }

    [Fact]
    public async Task Insurance_PrimaryAndSecondary_CanBeAddedUpdatedAndRemoved()
    {
        var created = await CreateAsync(Body(dob: "1990-05-14", insurances:
        [
            Policy("Primary", "Delta Dental", "DD123", relationship: "Self"),
            Policy("Secondary", "MetLife", "ML456", relationship: "Spouse", subscriber: "Jordan Smith", subscriberDob: "1988-02-01")
        ]));

        var primary = created.Insurances.Single(i => i.Priority == "Primary");
        Assert.Equal("Ava Smith", primary.SubscriberName);              // Self: the patient is the subscriber
        Assert.Equal(new DateOnly(1990, 5, 14), primary.SubscriberDateOfBirth);
        Assert.Equal("Jordan Smith", created.Insurances.Single(i => i.Priority == "Secondary").SubscriberName);

        // Update the primary's member ID and drop the secondary.
        var updated = await UpdateAsync(created.Id, Body(dob: "1990-05-14", insurances:
            [Policy("Primary", "Delta Dental", "DD999", relationship: "Self")]));
        var fetched = await GetAsync(created.Id);

        var kept = Assert.Single(fetched.Insurances);
        Assert.Equal(primary.Id, kept.Id);                               // updated in place, not replaced
        Assert.Equal("DD999", kept.MemberId);
        Assert.Single(updated.Insurances);

        // Add a secondary again (the soft-deleted one must not block the unique index).
        var readded = await UpdateAsync(created.Id, Body(dob: "1990-05-14", insurances:
        [
            Policy("Primary", "Delta Dental", "DD999", relationship: "Self"),
            Policy("Secondary", "Cigna", "CG1", relationship: "Child", subscriber: "Sam Smith")
        ]));
        Assert.Equal(2, readded.Insurances.Count);
    }

    [Fact]
    public async Task Insurance_UpdateWithoutTheList_LeavesItUnchanged()
    {
        var created = await CreateAsync(Body(insurances: [Policy("Primary", "Aetna", "AE1", relationship: "Self")]));

        await UpdateAsync(created.Id, Body(extra: new() { ["notes"] = "Prefers mornings" }));

        Assert.Single((await GetAsync(created.Id)).Insurances);
    }

    [Theory]
    [InlineData("Secondary", "Only a secondary")]                  // needs a primary
    [InlineData("Duplicate", "Two primaries")]
    [InlineData("NoSubscriber", "Spouse without a subscriber name")]
    public async Task Insurance_InvalidCombinations_Return400(string shape, string because)
    {
        object[] insurances = shape switch
        {
            "Secondary" => [Policy("Secondary", "Cigna", "C1", relationship: "Self")],
            "Duplicate" => [Policy("Primary", "Cigna", "C1", relationship: "Self"), Policy("Primary", "Aetna", "A1", relationship: "Self")],
            _ => [Policy("Primary", "Cigna", "C1", relationship: "Spouse")]
        };

        var response = await _api.Http.PostAsJsonAsync("/api/patients", Body(insurances: insurances), SchedulingApi.Json);

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, because);
    }

    [Fact]
    public async Task Lookups_ListCountriesWithTheUnitedStatesFirst()
    {
        var countries = await SchedulingApi.ReadAsync<List<JsonElement>>(await _api.Http.GetAsync("/api/lookups/countries"));

        Assert.Equal("US", countries[0].GetProperty("code").GetString());
    }

    private static Dictionary<string, object?> Body(
        string phone = "+1 212 555 0142", string? dob = null, object[]? insurances = null, Dictionary<string, object?>? extra = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["firstName"] = "Ava", ["lastName"] = "Smith", ["phone"] = phone, ["dateOfBirth"] = dob,
            ["insurances"] = insurances
        };
        foreach (var (key, value) in extra ?? []) body[key] = value;
        return body;
    }

    private static object Policy(string priority, string carrier, string memberId, string relationship,
        string? subscriber = null, string? subscriberDob = null) => new
    {
        priority, carrierName = carrier, memberId, groupNumber = "G-1", relationshipToPatient = relationship,
        subscriberName = subscriber, subscriberDateOfBirth = subscriberDob
    };

    private async Task<PatientDetail> CreateAsync(object body) =>
        await SchedulingApi.ReadAsync<PatientDetail>(await _api.Http.PostAsJsonAsync("/api/patients", body, SchedulingApi.Json));

    private async Task<PatientDetail> UpdateAsync(Guid id, object body) =>
        await SchedulingApi.ReadAsync<PatientDetail>(await _api.Http.PutAsJsonAsync($"/api/patients/{id}", body, SchedulingApi.Json));

    private async Task<PatientDetail> GetAsync(Guid id) =>
        await SchedulingApi.ReadAsync<PatientDetail>(await _api.Http.GetAsync($"/api/patients/{id}"));
}
