using DentalAI.Infrastructure.Notifications;
using Xunit;

namespace DentalAI.Api.IntegrationTests;

/// <summary>Twilio's request signing: HMAC-SHA1 of the URL plus the form fields sorted by name.</summary>
public sealed class TwilioSignatureTests
{
    private const string Token = "secret-token";
    private const string Url = "https://api.example.com/api/webhooks/twilio/sms";

    private static readonly KeyValuePair<string, string>[] Form =
    [
        new("From", "+12125550142"), new("Body", "C"), new("To", "+12125550100"), new("MessageSid", "SM123")
    ];

    [Fact]
    public void ValidSignature_IsAccepted_WhateverTheFieldOrder()
    {
        var signature = TwilioSignature.Compute(Token, Url, Form);

        Assert.True(TwilioSignature.IsValid(Token, Url, Form.Reverse(), signature));
    }

    [Fact]
    public void ChangedBodyUrlOrToken_IsRejected()
    {
        var signature = TwilioSignature.Compute(Token, Url, Form);
        var tampered = Form.Select(x => x.Key == "Body" ? new KeyValuePair<string, string>("Body", "STOP") : x);

        Assert.False(TwilioSignature.IsValid(Token, Url, tampered, signature));
        Assert.False(TwilioSignature.IsValid(Token, Url + "?x=1", Form, signature));
        Assert.False(TwilioSignature.IsValid("other-token", Url, Form, signature));
        Assert.False(TwilioSignature.IsValid(Token, Url, Form, null));
        Assert.False(TwilioSignature.IsValid(null!, Url, Form, signature)); // not configured: refuse everything
    }
}
