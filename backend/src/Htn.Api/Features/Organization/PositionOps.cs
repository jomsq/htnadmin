using Htn.Domain.Organization;
using Htn.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Organization;

internal static class PositionOps
{
    /// <summary>
    /// Vacates the positions <paramref name="person"/> holds that match <paramref name="which"/> (e.g. the ones that no
    /// longer fit after a level change). Closes each open term and writes PositionVacated history. Caller saves.
    /// Returns the vacated titles for the audit entry.
    /// </summary>
    public static async Task<List<string>> VacateAsync(
        HtnDbContext db, Person person, Func<Position, bool> which, DateTimeOffset at, Guid? actorId, string reason, CancellationToken ct)
    {
        var held = await db.Positions.Where(p => p.HolderId == person.Id).ToListAsync(ct);
        var vacated = new List<string>();
        foreach (var position in held.Where(which))
        {
            var term = await OpenTermAsync(db, position, ct);
            position.Vacate(person, term, at, actorId, reason);
            vacated.Add(position.Title);
        }
        return vacated;
    }

    /// <summary>The open assignment row for a held position (created if a seeded/legacy holder has none).</summary>
    public static async Task<PositionAssignment> OpenTermAsync(HtnDbContext db, Position position, CancellationToken ct) =>
        await db.PositionAssignments.SingleOrDefaultAsync(a => a.PositionId == position.Id && a.EndedAt == null, ct)
        ?? throw new InvalidOperationException($"Position {position.Id} has a holder but no open assignment.");
}
