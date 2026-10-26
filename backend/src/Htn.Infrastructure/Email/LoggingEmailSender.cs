using Htn.Domain.Common;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Htn.Infrastructure.Email;

/// <summary>
/// Used when MAILTRAP_API_TOKEN is not set. In Development it logs the recipient and the plain-text body so
/// invite/reset links can be followed locally. Elsewhere (only allowed with Email:AllowLoggingSender=true) it logs
/// the subject and a masked recipient, never the body, which contains single-use links.
/// </summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger, IHostEnvironment env) : IEmailSender
{
    public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct)
    {
        // Names and sizes only: attachment bytes (QR codes, logos) never go to the log.
        var attachments = message.AllAttachments.Count == 0
            ? "none"
            : string.Join(", ", message.AllAttachments.Select(a =>
                $"{a.FileName} ({a.ContentType}, {a.Content.Length} bytes{(a.IsInline ? $", inline cid:{a.ContentId}" : "")})"));
        if (env.IsDevelopment())
            logger.LogInformation("Email not sent (no MAILTRAP_API_TOKEN). To {To}, subject {Subject}, attachments: {Attachments}.\n{Text}",
                message.To, message.Subject, attachments, message.Text);
        else
            logger.LogWarning("Email not sent (no MAILTRAP_API_TOKEN). To {To}, subject {Subject}, {Count} attachment(s).",
                EmailMask.Mask(message.To), message.Subject, message.AllAttachments.Count);
        return Task.FromResult(new EmailSendResult(null));
    }
}
