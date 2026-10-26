namespace Htn.Infrastructure.Email;

public sealed record EmailMessage(string To, string? ToName, string Subject, string Html, string Text, string Category);

public sealed record EmailSendResult(string? ExternalId);

public interface IEmailSender
{
    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct);
}

public static class EmailCategories
{
    public const string Invite = "invite";
    public const string PasswordReset = "password-reset";
}
