using System.Security;
using DentalAI.Application.Common.Phone;
using DentalAI.Application.Services;
using DentalAI.Infrastructure.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DentalAI.Api.Controllers;

/// <summary>
/// Patients' replies to texts, posted by Twilio. Every request must carry a valid X-Twilio-Signature (checked
/// against the configured auth token and public webhook URL), otherwise anyone could opt a number out or confirm
/// someone's appointment. Answers with TwiML.
/// </summary>
[AllowAnonymous]
[ApiController]
[Route("api/webhooks/twilio")]
public sealed class TwilioWebhookController(
    InboundSmsHandler handler,
    IOptions<NotificationOptions> options,
    ILogger<TwilioWebhookController> logger) : ControllerBase
{
    [HttpPost("sms")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> InboundSms(CancellationToken cancellationToken)
    {
        var twilio = options.Value.Sms.Twilio;
        var form = await Request.ReadFormAsync(cancellationToken);
        var fields = form.Select(x => new KeyValuePair<string, string>(x.Key, x.Value.ToString())).ToList();
        var url = twilio.InboundWebhookUrl ?? $"{Request.Scheme}://{Request.Host}{Request.Path}{Request.QueryString}";

        if (!TwilioSignature.IsValid(twilio.AuthToken, url, fields, Request.Headers["X-Twilio-Signature"].ToString()))
        {
            logger.LogWarning("Rejected a Twilio webhook with a missing or invalid signature");
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var from = form["From"].ToString();
        if (!PhoneNumberRules.TryNormalize(from, null, out var e164)) return Twiml(null);
        var reply = await handler.HandleAsync(e164, form["Body"].ToString(), cancellationToken);
        return Twiml(reply);
    }

    private ContentResult Twiml(string? reply) => Content(
        reply is null
            ? "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Response/>"
            : $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><Response><Message>{SecurityElement.Escape(reply)}</Message></Response>",
        "text/xml");
}
