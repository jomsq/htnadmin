namespace Htn.Infrastructure.Email;

/// <summary>Outbox message types handled by the email dispatcher.</summary>
public static class EmailTypes
{
    public const string Invite = "email.invite";
    public const string PasswordReset = "email.password_reset";

    /// <summary>Someone was @mentioned in a task comment (frame 08). Queued only for people who can see that board.</summary>
    public const string TaskMention = "email.task_mention";
}

/// <summary>Payload stored in the outbox. The raw token travels separately in <c>OutboxMessage.Secret</c>.</summary>
public sealed record InviteEmailPayload(Guid UserId, string To, string FirstName, string? InviterName, string Level, string? Position);

public sealed record PasswordResetEmailPayload(Guid UserId, string To, string FirstName);

/// <summary>
/// One @mention in one task comment. The recipient was already checked against the board's visibility when the comment
/// was posted; the renderer re-checks that the person still has an active login and that the comment still stands.
/// </summary>
public sealed record TaskMentionEmailPayload(Guid CommentId, Guid TaskId, Guid PersonId, string To, string FirstName);

public static class EmailText
{
    public static string WithArticle(string word) =>
        (word.Length > 0 && "AEIOUaeiou".Contains(word[0]) ? "an " : "a ") + word;
}

public static class Brand
{
    public const string OrgName = "HackTheNorth.PH";
    public const string Color = "#B71C1C";
}
