using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DentalAI.Application.Common.Notifications;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace DentalAI.Infrastructure.Notifications;

/// <summary>Development: writes the email to the log instead of sending it.</summary>
public sealed class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
{
    public bool IsLive => false;

    public Task<SendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        logger.LogInformation("[email not sent: no provider] To {To} | {Subject}\n{Body}", message.To, message.Subject, message.TextBody);
        return Task.FromResult(SendResult.Sent($"log-{Guid.NewGuid():N}"));
    }
}

/// <summary>Development: writes the text to the log instead of sending it.</summary>
public sealed class LogSmsSender(ILogger<LogSmsSender> logger) : ISmsSender
{
    public bool IsLive => false;

    public Task<SendResult> SendAsync(string toE164, string body, CancellationToken cancellationToken)
    {
        logger.LogInformation("[SMS not sent: no provider] To {To} | {Body}", toE164, body);
        return Task.FromResult(SendResult.Sent($"log-{Guid.NewGuid():N}"));
    }
}

public sealed class SmtpEmailSender(IOptions<NotificationOptions> options) : IEmailSender
{
    public bool IsLive => true;

    public async Task<SendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var config = options.Value.Email;
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(message.FromName ?? config.FromName, config.FromAddress));
        if (!MailboxAddress.TryParse(message.To, out var to)) return SendResult.GiveUp($"Invalid email address: {message.To}");
        mime.To.Add(to);
        if (message.ReplyTo is not null && MailboxAddress.TryParse(message.ReplyTo, out var replyTo)) mime.ReplyTo.Add(replyTo);
        mime.Subject = message.Subject;
        mime.Body = new TextPart("plain") { Text = message.TextBody };

        using var client = new SmtpClient();
        try
        {
            var security = config.Smtp.Port == 465 ? SecureSocketOptions.SslOnConnect
                : config.Smtp.UseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
            await client.ConnectAsync(config.Smtp.Host, config.Smtp.Port, security, cancellationToken);
            if (!string.IsNullOrEmpty(config.Smtp.Username))
                await client.AuthenticateAsync(config.Smtp.Username, config.Smtp.Password ?? string.Empty, cancellationToken);
            var response = await client.SendAsync(mime, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
            return SendResult.Sent(mime.MessageId ?? response);
        }
        catch (SmtpCommandException exception) when (exception.ErrorCode is SmtpErrorCode.RecipientNotAccepted)
        {
            return SendResult.GiveUp($"Recipient refused: {exception.Message}");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return SendResult.Retry(exception.Message);
        }
    }
}

/// <summary>Twilio Programmable Messaging over its REST API (no SDK needed).</summary>
public sealed class TwilioSmsSender(HttpClient http, IOptions<NotificationOptions> options) : ISmsSender
{
    // Twilio error codes that won't succeed on retry: invalid "To", unsubscribed (STOP), not a mobile, blocked, etc.
    private static readonly HashSet<int> PermanentErrors = [21211, 21212, 21408, 21610, 21612, 21614, 21606, 30003, 30004, 30005, 30006];

    public bool IsLive => true;

    public async Task<SendResult> SendAsync(string toE164, string body, CancellationToken cancellationToken)
    {
        var config = options.Value.Sms.Twilio;
        var form = new Dictionary<string, string> { ["To"] = toE164, ["Body"] = body };
        if (!string.IsNullOrEmpty(config.MessagingServiceSid)) form["MessagingServiceSid"] = config.MessagingServiceSid;
        else if (!string.IsNullOrEmpty(config.FromNumber)) form["From"] = config.FromNumber;
        else return SendResult.GiveUp("Twilio is not configured with a Messaging Service or a From number.");

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://api.twilio.com/2010-04-01/Accounts/{Uri.EscapeDataString(config.AccountSid)}/Messages.json")
        {
            Content = new FormUrlEncodedContent(form)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{config.AccountSid}:{config.AuthToken}")));

        using var response = await http.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        var root = document.RootElement;

        if (response.IsSuccessStatusCode)
            return SendResult.Sent(root.TryGetProperty("sid", out var sid) ? sid.GetString() : null);

        var code = root.TryGetProperty("code", out var c) && c.TryGetInt32(out var n) ? n : 0;
        var message = root.TryGetProperty("message", out var m) ? m.GetString() : response.ReasonPhrase;
        var error = $"Twilio {code}: {message}";
        var retryable = response.StatusCode is HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError;
        return PermanentErrors.Contains(code) || (!retryable && code != 0) ? SendResult.GiveUp(error) : SendResult.Retry(error);
    }
}

public sealed class AppLinks(IOptions<NotificationOptions> options) : IAppLinks
{
    public string AcceptInvite(string token) =>
        $"{options.Value.WebBaseUrl.TrimEnd('/')}/auth/accept-invite?token={Uri.EscapeDataString(token)}";
}
