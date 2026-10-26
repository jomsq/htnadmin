using Htn.Domain.Committees;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Email.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Htn.Infrastructure.Email;

/// <summary>
/// Builds the @mention email from the current database state when the outbox sends it (frame 08's comment box).
/// Returns null when the mention no longer applies — the comment was deleted, the task or the person is gone, or the
/// person lost their login — so a stale or unwanted message is never delivered.
/// </summary>
public sealed class TaskEmails(HtnDbContext db, EmailRenderer renderer, IOptions<AppLinkOptions> links)
{
    /// <summary>How much of the comment travels in the email; the rest stays behind the login.</summary>
    public const int ExcerptMaxLength = 300;

    public async Task<EmailMessage?> MentionAsync(TaskMentionEmailPayload p)
    {
        var comment = await db.TaskComments.AsNoTracking().SingleOrDefaultAsync(c => c.Id == p.CommentId);
        if (comment is null || comment.DeletedAt is not null) return null;
        var task = await db.Tasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == comment.TaskId);
        if (task is null) return null;
        // The recipient must still be a person with an active login.
        var person = await (
            from pr in db.People.AsNoTracking()
            join u in db.Users.AsNoTracking() on (Guid?)pr.Id equals u.PersonId
            where pr.Id == p.PersonId && pr.IsActive && u.IsActive
            select new { pr.FirstName }).FirstOrDefaultAsync();
        if (person is null) return null;

        var ev = await db.Events.AsNoTracking().Where(e => e.Id == task.EventId).Select(e => e.Name).SingleAsync();
        var committee = await db.Committees.AsNoTracking().Where(c => c.Id == task.CommitteeId).Select(c => c.Name).SingleAsync();
        var author = await (
            from u in db.Users.AsNoTracking()
            join pr in db.People.AsNoTracking() on u.PersonId equals (Guid?)pr.Id
            where u.Id == comment.AuthorId
            select pr.FirstName + " " + pr.LastName).FirstOrDefaultAsync() ?? "A teammate";

        var excerpt = comment.Body.Length > ExcerptMaxLength ? comment.Body[..ExcerptMaxLength] + "…" : comment.Body;
        var link = links.Value.Task(task.EventId, task.Id);
        var logo = await renderer.LogoAsync();
        var html = await renderer.RenderAsync<TaskMentionEmail>(new Dictionary<string, object?>
        {
            [nameof(TaskMentionEmail.FirstName)] = person.FirstName,
            [nameof(TaskMentionEmail.MentionedBy)] = author,
            [nameof(TaskMentionEmail.Reference)] = task.Reference,
            [nameof(TaskMentionEmail.TaskTitle)] = task.Title,
            [nameof(TaskMentionEmail.EventName)] = ev,
            [nameof(TaskMentionEmail.CommitteeName)] = committee,
            [nameof(TaskMentionEmail.Excerpt)] = excerpt,
            [nameof(TaskMentionEmail.Link)] = link,
            [nameof(TaskMentionEmail.BrandColor)] = await renderer.BrandColorAsync(),
            [nameof(TaskMentionEmail.LogoCid)] = logo?.ContentId,
        });
        var text =
            $"""
            Hi {person.FirstName},

            {author} mentioned you in a comment on {task.Reference} · {task.Title} ({committee}, {ev}).

            "{excerpt}"

            Open the task:
            {link}

            {Brand.OrgName}
            """;
        return new EmailMessage(p.To, person.FirstName, $"{author} mentioned you on {task.Reference}", html, text,
            EmailCategories.TaskMention, logo is null ? null : [logo]);
    }
}
