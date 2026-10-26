using Htn.Domain.Organization;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Email;
using Htn.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Tasks;

/// <summary>
/// @mentions in task comments. An email goes out through the outbox, and <b>only</b> to people who can already see
/// that board: the board of trustees, or someone on an active committee of the task's event. Anyone else in the list
/// is dropped silently — the response never says who was mailed and never carries an email address, so a comment can't
/// be used to probe for accounts.
/// </summary>
internal static class TaskMentions
{
    /// <summary>A mention recipient: who they are and where the email goes.</summary>
    private sealed record Recipient(Guid PersonId, string Email, string FirstName);

    /// <summary>
    /// Queues one <c>email.task_mention</c> message per person who may see the board, inside the caller's unit of work.
    /// Returns the person ids that were queued (for the audit trail, never for the response).
    /// </summary>
    public static async Task<List<Guid>> QueueAsync(
        HtnDbContext db, Guid eventId, Guid commentId, Guid taskId, IReadOnlyCollection<Guid> personIds, Guid? authorPersonId,
        DateTimeOffset at, CancellationToken ct)
    {
        if (personIds.Count == 0) return [];
        var targets = personIds.Where(id => id != authorPersonId).Distinct().ToList(); // nobody is emailed about their own comment
        if (targets.Count == 0) return [];

        var candidates = await (
            from p in db.People.AsNoTracking()
            join u in db.Users.AsNoTracking() on (Guid?)p.Id equals u.PersonId
            where targets.Contains(p.Id) && p.IsActive && u.IsActive && u.Email != null
            select new { p.Id, p.Level, p.FirstName, Email = u.Email! }).ToListAsync(ct);

        // Board members are admins on every board; everyone else must be on a committee of this event.
        var onEvent = await db.CommitteeMembers.AsNoTracking()
            .Where(m => m.EventId == eventId && m.LeftAt == null
                        && db.Committees.Any(c => c.Id == m.CommitteeId && c.IsActive))
            .Select(m => m.PersonId).ToListAsync(ct);

        var queued = new List<Guid>();
        foreach (var c in candidates)
        {
            if (c.Level != PersonLevel.Board && !onEvent.Contains(c.Id)) continue; // silently dropped
            db.Outbox.Add(OutboxProcessor.TaskMentionEmail(
                new TaskMentionEmailPayload(commentId, taskId, c.Id, c.Email, c.FirstName), at));
            queued.Add(c.Id);
        }
        return queued;
    }
}
