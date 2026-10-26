using Htn.Api.Auth;
using Htn.Domain.Committees;
using Htn.Domain.Events;
using Htn.Domain.Organization;
using Htn.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Committees;

/// <summary>DTO projections, roster lookups and audit snapshots shared by the committee and volunteer endpoints.</summary>
internal static class CommitteeViews
{
    /// <summary>Levels that may be put on a committee: they are the ones with a login.</summary>
    public static readonly PersonLevel[] MemberLevels = [PersonLevel.Board, PersonLevel.Goon, PersonLevel.Apprentice];

    /// <summary>The event, or null when the caller may not see it (Drafts are hidden without Events.Manage).</summary>
    public static async Task<Event?> VisibleEventAsync(
        HttpContext http, IAuthorizationService auth, HtnDbContext db, Guid eventId, CancellationToken ct)
    {
        var ev = await db.Events.AsNoTracking().SingleOrDefaultAsync(e => e.Id == eventId, ct);
        if (ev is null) return null;
        if (ev.Status == EventStatus.Draft && !await Events.EventViews.CanManageAsync(http, auth)) return null;
        return ev;
    }

    public sealed record RosterPerson(Guid Id, string FirstName, string LastName, PersonLevel Level);

    /// <summary>Active people of the event roster who hold a login, by person id.</summary>
    public static async Task<Dictionary<Guid, RosterPerson>> RosterAsync(HtnDbContext db, CancellationToken ct) =>
        await db.People.AsNoTracking()
            .Where(p => p.IsActive && MemberLevels.Contains(p.Level) && db.Users.Any(u => u.PersonId == p.Id && u.IsActive))
            .Select(p => new RosterPerson(p.Id, p.FirstName, p.LastName, p.Level))
            .ToDictionaryAsync(p => p.Id, ct);

    /// <summary>
    /// The whole screen. <paramref name="manageAll"/> is true for the board (admins everywhere); otherwise a committee
    /// is manageable only by its lead. Volunteer contact details are stripped for callers who can't manage them.
    /// </summary>
    public static async Task<CommitteesPageDto> PageAsync(
        HtnDbContext db, ICommitteeContext scope, ICommitteeOpenTasks openTasks, Guid eventId, bool manageAll, CancellationToken ct)
    {
        var committees = await db.Committees.AsNoTracking().Where(c => c.EventId == eventId)
            .OrderByDescending(c => c.IsActive).ThenBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct);
        var members = await db.CommitteeMembers.AsNoTracking().Where(m => m.EventId == eventId && m.LeftAt == null)
            .OrderBy(m => m.JoinedAt).ThenBy(m => m.Id).ToListAsync(ct);
        var volunteers = await db.EventVolunteers.AsNoTracking().Where(v => v.EventId == eventId && v.IsActive)
            .OrderBy(v => v.Name).ThenBy(v => v.Id).ToListAsync(ct);
        var roster = await RosterAsync(db, ct);
        var counts = await openTasks.ForEventAsync(eventId, ct);

        var cards = new List<CommitteeDto>(committees.Count);
        foreach (var committee in committees)
        {
            var canManage = manageAll || await scope.IsLeadOfAsync(committee.Id, ct);
            var mine = members.Where(m => m.CommitteeId == committee.Id).ToList();
            var leadPerson = committee.LeadPersonId is { } leadId && roster.TryGetValue(leadId, out var lead)
                ? new CommitteePersonDto(lead.Id, lead.FirstName, lead.LastName, lead.Level)
                : null;
            var cardVolunteers = volunteers.Where(v => v.CommitteeId == committee.Id).Select(v => Volunteer(v, canManage)).ToList();
            cards.Add(new CommitteeDto(
                committee.Id, committee.Name, committee.SortOrder, committee.IsActive, canManage,
                counts.TryGetValue(committee.Id, out var open) ? open : 0,
                mine.Count + cardVolunteers.Count,
                leadPerson,
                mine.Where(m => m.PersonId != committee.LeadPersonId)
                    .Select(m => roster.TryGetValue(m.PersonId, out var p)
                        ? new CommitteeMemberDto(p.Id, p.FirstName, p.LastName, p.Level, m.JoinedAt)
                        : null)
                    .OfType<CommitteeMemberDto>().ToList(),
                cardVolunteers));
        }

        // The pool: active goons and apprentices with a login who are on no committee of this event (board members are
        // admins everywhere and are never pooled), plus volunteers not attached to a committee.
        var assigned = members.Select(m => m.PersonId).ToHashSet();
        var pool = new CommitteePoolDto(
            roster.Values
                .Where(p => p.Level is PersonLevel.Goon or PersonLevel.Apprentice && !assigned.Contains(p.Id))
                .OrderBy(p => p.FirstName).ThenBy(p => p.LastName).ThenBy(p => p.Id)
                .Select(p => new CommitteePersonDto(p.Id, p.FirstName, p.LastName, p.Level)).ToList(),
            volunteers.Where(v => v.CommitteeId is null).Select(v => Volunteer(v, manageAll)).ToList());

        return new CommitteesPageDto(manageAll, manageAll || await scope.LeadsAnyCommitteeOfEventAsync(eventId, ct), cards, pool);
    }

    /// <summary>One committee card, for the create / update / membership responses.</summary>
    public static async Task<CommitteeDto> CardAsync(
        HtnDbContext db, ICommitteeOpenTasks openTasks, Committee committee, bool canManage, CancellationToken ct)
    {
        var members = await db.CommitteeMembers.AsNoTracking()
            .Where(m => m.CommitteeId == committee.Id && m.LeftAt == null).OrderBy(m => m.JoinedAt).ThenBy(m => m.Id).ToListAsync(ct);
        var volunteers = await db.EventVolunteers.AsNoTracking()
            .Where(v => v.CommitteeId == committee.Id && v.IsActive).OrderBy(v => v.Name).ThenBy(v => v.Id).ToListAsync(ct);
        var roster = await RosterAsync(db, ct);
        var cardVolunteers = volunteers.Select(v => Volunteer(v, canManage)).ToList();
        var leadPerson = committee.LeadPersonId is { } leadId && roster.TryGetValue(leadId, out var lead)
            ? new CommitteePersonDto(lead.Id, lead.FirstName, lead.LastName, lead.Level)
            : null;
        return new CommitteeDto(
            committee.Id, committee.Name, committee.SortOrder, committee.IsActive, canManage,
            await openTasks.ForCommitteeAsync(committee.Id, ct),
            members.Count + cardVolunteers.Count,
            leadPerson,
            members.Where(m => m.PersonId != committee.LeadPersonId)
                .Select(m => roster.TryGetValue(m.PersonId, out var p)
                    ? new CommitteeMemberDto(p.Id, p.FirstName, p.LastName, p.Level, m.JoinedAt)
                    : null)
                .OfType<CommitteeMemberDto>().ToList(),
            cardVolunteers);
    }

    /// <summary>Volunteer contact details are personal data: only a manager of that volunteer sees them.</summary>
    public static VolunteerDto Volunteer(EventVolunteer v, bool canManage) =>
        new(v.Id, v.Name, canManage ? v.Email : null, canManage ? v.Mobile : null);

    public static object Audit(Committee c) => new
    {
        c.EventId, c.Name, c.SortOrder, c.IsActive, c.LeadPersonId,
    };

    public static object Audit(CommitteeMember m) => new
    {
        m.CommitteeId, m.EventId, m.PersonId, Role = m.Role.ToString(), m.JoinedAt, m.LeftAt,
    };

    public static object Audit(EventVolunteer v) => new
    {
        v.EventId, v.Name, v.Email, v.Mobile, v.CommitteeId, v.IsActive,
    };
}
