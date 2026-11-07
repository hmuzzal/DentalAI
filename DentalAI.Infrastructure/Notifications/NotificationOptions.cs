namespace DentalAI.Infrastructure.Notifications;

/// <summary>
/// "Notifications" configuration. Providers default to "Log" (messages are written to the server log, nothing is
/// delivered), so development needs no accounts. Put real credentials in user-secrets or environment variables,
/// never in appsettings.json:
///   dotnet user-secrets set "Notifications:Email:Smtp:Password" "..."
///   dotnet user-secrets set "Notifications:Sms:Twilio:AuthToken" "..."
/// </summary>
public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>The web app's address, for links in emails, e.g. https://app.dentalai.com.</summary>
    public string WebBaseUrl { get; set; } = "http://localhost:4200";

    /// <summary>Runs the reminder scheduler and the sender in this API process. Off in integration tests.</summary>
    public bool BackgroundJobsEnabled { get; set; } = true;
    public int DispatchIntervalSeconds { get; set; } = 10;
    public int ReminderIntervalSeconds { get; set; } = 60;

    public EmailOptions Email { get; set; } = new();
    public SmsOptions Sms { get; set; } = new();
}

public sealed class EmailOptions
{
    /// <summary>"Log" or "Smtp".</summary>
    public string Provider { get; set; } = "Log";
    public string FromAddress { get; set; } = "no-reply@dentalai.local";
    public string FromName { get; set; } = "DentalAI";
    public SmtpOptions Smtp { get; set; } = new();
}

/// <summary>Any SMTP server: SendGrid / Mailgun / SES SMTP relays, Microsoft 365, or Mailpit locally.</summary>
public sealed class SmtpOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    /// <summary>STARTTLS on 587 (default); implicit TLS on 465.</summary>
    public bool UseSsl { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }
}

public sealed class SmsOptions
{
    /// <summary>"Log" or "Twilio".</summary>
    public string Provider { get; set; } = "Log";
    public TwilioOptions Twilio { get; set; } = new();
}

/// <summary>
/// Twilio. Texting US numbers needs A2P 10DLC brand and campaign registration (or a verified toll-free number);
/// a non-US business registers with its own country's tax ID.
/// </summary>
public sealed class TwilioOptions
{
    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    /// <summary>A Messaging Service (recommended: handles opt-outs and number pools) or a single From number.</summary>
    public string? MessagingServiceSid { get; set; }
    public string? FromNumber { get; set; }
    /// <summary>
    /// The public URL Twilio posts replies to, exactly as configured in Twilio (used to check the signature), e.g.
    /// https://api.dentalai.com/api/webhooks/twilio/sms.
    /// </summary>
    public string? InboundWebhookUrl { get; set; }
}
