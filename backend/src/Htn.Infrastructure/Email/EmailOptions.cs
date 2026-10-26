using System.Net.Mail;
using Microsoft.Extensions.Configuration;

namespace Htn.Infrastructure.Email;

/// <summary>Mailtrap settings. Read only from environment variables / configuration, never committed.</summary>
public sealed class MailtrapOptions
{
    public const string LiveUrl = "https://send.api.mailtrap.io/api/send";
    public const string SandboxUrlFormat = "https://sandbox.api.mailtrap.io/api/send/{0}";

    public string? ApiToken { get; set; }
    public string Mode { get; set; } = "sandbox";
    public string? InboxId { get; set; }
    public string From { get; set; } = "HackTheNorth.PH <no-reply@hackthenorth.ph>";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiToken);
    public bool IsLive => string.Equals(Mode, "live", StringComparison.OrdinalIgnoreCase);

    public Uri SendUri()
    {
        if (IsLive) return new Uri(LiveUrl);
        if (string.IsNullOrWhiteSpace(InboxId))
            throw new InvalidOperationException("MAILTRAP_INBOX_ID is required when MAILTRAP_MODE is sandbox.");
        return new Uri(string.Format(System.Globalization.CultureInfo.InvariantCulture, SandboxUrlFormat, Uri.EscapeDataString(InboxId)));
    }

    public MailAddress FromAddress() => new(From);

    public static MailtrapOptions FromConfiguration(IConfiguration config) => new()
    {
        ApiToken = config["MAILTRAP_API_TOKEN"],
        Mode = config["MAILTRAP_MODE"] is { Length: > 0 } m ? m : "sandbox",
        InboxId = config["MAILTRAP_INBOX_ID"],
        From = config["MAIL_FROM"] is { Length: > 0 } f ? f : "HackTheNorth.PH <no-reply@hackthenorth.ph>",
    };
}

public sealed class AppLinkOptions
{
    public const string Section = "App";
    public string PublicBaseUrl { get; set; } = "http://localhost:5173";

    public string Invite(string token) => $"{PublicBaseUrl.TrimEnd('/')}/invite/{Uri.EscapeDataString(token)}";
    public string Reset(string token) => $"{PublicBaseUrl.TrimEnd('/')}/reset/{Uri.EscapeDataString(token)}";
}
