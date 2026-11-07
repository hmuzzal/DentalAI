namespace DentalAI.Application.Common.Notifications;

/// <summary>The outcome of one send. <c>Permanent</c> failures (bad address, unsubscribed number) aren't retried.</summary>
public sealed record SendResult(bool Success, string? ProviderMessageId = null, string? Error = null, bool Permanent = false)
{
    public static SendResult Sent(string? providerId) => new(true, providerId);
    public static SendResult Retry(string error) => new(false, Error: error);
    public static SendResult GiveUp(string error) => new(false, Error: error, Permanent: true);
}

public sealed record EmailMessage(string To, string Subject, string TextBody, string? ReplyTo = null, string? FromName = null);

/// <summary>Sends email (SMTP, or "Log" in development).</summary>
public interface IEmailSender
{
    /// <summary>False when no real provider is configured: messages are only written to the log.</summary>
    bool IsLive { get; }
    Task<SendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>Sends text messages (Twilio, or "Log" in development).</summary>
public interface ISmsSender
{
    bool IsLive { get; }
    Task<SendResult> SendAsync(string toE164, string body, CancellationToken cancellationToken);
}

/// <summary>Links into the web app, for emails (e.g. the staff invite link).</summary>
public interface IAppLinks
{
    string AcceptInvite(string token);
}
