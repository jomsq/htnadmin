using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Htn.Infrastructure.Email;

/// <summary>
/// Used when MAILTRAP_API_TOKEN is not set. Logs subject and recipient only.
/// In Development it also logs the plain-text body so invite/reset links can be followed locally.
/// </summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger, IHostEnvironment env) : IEmailSender
{
    public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct)
    {
        if (env.IsDevelopment())
            logger.LogInformation("Email not sent (no MAILTRAP_API_TOKEN). To {To}, subject {Subject}.\n{Text}", message.To, message.Subject, message.Text);
        else
            logger.LogWarning("Email not sent (no MAILTRAP_API_TOKEN). To {To}, subject {Subject}.", message.To, message.Subject);
        return Task.FromResult(new EmailSendResult(null));
    }
}
