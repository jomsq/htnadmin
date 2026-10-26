namespace Htn.Infrastructure.Email;

/// <summary>
/// A file sent with an email. With a <see cref="ContentId"/> it is an inline image referenced from the HTML as
/// <c>cid:{ContentId}</c>; without one it is a regular attachment.
/// </summary>
public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content, string? ContentId = null)
{
    public bool IsInline => ContentId is not null;
}

public sealed record EmailMessage(
    string To, string? ToName, string Subject, string Html, string Text, string Category,
    IReadOnlyList<EmailAttachment>? Attachments = null)
{
    public IReadOnlyList<EmailAttachment> AllAttachments => Attachments ?? [];
}

public sealed record EmailSendResult(string? ExternalId);

public interface IEmailSender
{
    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct);
}

public static class EmailCategories
{
    public const string Invite = "invite";
    public const string PasswordReset = "password-reset";
    public const string Billing = "billing";
    public const string Ticket = "ticket";
    public const string Registration = "registration";
    public const string TaskMention = "task-mention";
}
