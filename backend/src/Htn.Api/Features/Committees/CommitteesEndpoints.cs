using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Api.Features.Events;
using Htn.Domain.Committees;
using Htn.Domain.Organization;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Committees;

/// <summary>
/// Committees per event (frame 06). Reading needs <c>Events.View</c>; every write needs
/// <c>Committees.ManageMembers</c>, which is resource-scoped: the board may change every committee, a lead only their
/// own. Creating, renaming, re-leading and deactivating a committee is board-only, so those routes carry a second
/// board gate.
/// <para>
/// <b>How the scope is enforced.</b> The route policy is the coarse gate (it keeps out anyone who can never hold the
/// right: apprentices, and goons who lead nothing). The committee only becomes known once the row is loaded, so the
/// handler then runs the same policy against a <see cref="CommitteeScope"/> resource and returns 403 through
/// <see cref="ScopedAuthorization.DenyAsync"/>. Payloads carry per-committee <c>canManage</c> flags for the UI.
/// </para>
/// <para>
/// Cross-row invariants (unique name per event, one active committee per person, one lead per committee, the
/// open-task guard) are read and written under the event row lock — the event row first, always (see
/// <see cref="EventLock"/>).
/// </para>
/// </summary>
public static class CommitteesEndpoints
{
    public static RouteGroupBuilder MapCommittees(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/events/{eventId:guid}/committees").WithTags("Committees");
        g.MapGet("/", List).RequireAuthorization(Policies.EventsView);
        // Every mutating route is metered per signed-in user (see TaskRateLimiting).
        g.MapPost("/", Create).RequireAuthorization(Policies.CommitteesManageMembers).RequireAuthorization(Policies.BoardOnly)
            .RequireRateLimiting(TaskRateLimiting.Write).Validate<CommitteeRequest>();
        g.MapPut("/{committeeId:guid}", Update).RequireAuthorization(Policies.CommitteesManageMembers)
            .RequireAuthorization(Policies.BoardOnly)
            .RequireRateLimiting(TaskRateLimiting.Write).Validate<CommitteeRequest>();
        g.MapPost("/{committeeId:guid}/deactivate", Deactivate).RequireAuthorization(Policies.CommitteesManageMembers)
            .RequireAuthorization(Policies.BoardOnly)
            .RequireRateLimiting(TaskRateLimiting.Write).Validate<CommitteeReasonRequest>();
        g.MapPut("/{committeeId:guid}/lead", SetLead).RequireAuthorization(Policies.CommitteesManageMembers)
            .RequireAuthorization(Policies.BoardOnly)
            .RequireRateLimiting(TaskRateLimiting.Write).Validate<CommitteeLeadRequest>();
        g.MapPost("/{committeeId:guid}/members", AddMember).RequireAuthorization(Policies.CommitteesManageMembers)
            .RequireRateLimiting(TaskRateLimiting.Write).Validate<CommitteeMemberRequest>();
        g.MapDelete("/{committeeId:guid}/members/{personId:guid}", RemoveMember)
            .RequireAuthorization(Policies.CommitteesManageMembers)
            .RequireRateLimiting(TaskRateLimiting.Write);
        g.MapPost("/{committeeId:guid}/volunteers/{volunteerId:guid}", AttachVolunteer)
            .RequireAuthorization(Policies.CommitteesManageMembers)
            .RequireRateLimiting(TaskRateLimiting.Write);
        g.MapDelete("/{committeeId:guid}/volunteers/{volunteerId:guid}", DetachVolunteer)
            .RequireAuthorization(Policies.CommitteesManageMembers)
            .RequireRateLimiting(TaskRateLimiting.Write);
        return g;
    }

    // ---------- Read ----------

    private static async Task<IResult> List(
        Guid eventId, HttpContext http, IAuthorizationService auth, HtnDbContext db, ICommitteeContext scope,
        ICommitteeOpenTasks openTasks, CancellationToken ct)
    {
        if (await CommitteeViews.VisibleEventAsync(http, auth, db, eventId, ct) is null) return TypedResults.NotFound();
        var board = http.User.IsInRole(Roles.Board);
        return TypedResults.Ok(await CommitteeViews.PageAsync(db, scope, openTasks, eventId, board, ct));
    }

    // ---------- Committee (board only) ----------

    private static async Task<IResult> Create(
        Guid eventId, CommitteeRequest req, HtnDbContext db, ICommitteeOpenTasks openTasks, IAuditLog audit, ICurrentUser me,
        TimeProvider clock, CancellationToken ct)
    {
        // Unique name per event and the next sort order are cross-row reads: under the event lock.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await EventLock.AcquireAsync(db, eventId, ct)) return TypedResults.NotFound();
        var name = req.Name.Trim();
        if (await ExistsByNameAsync(db, eventId, name, null, ct)) return Duplicate();

        var sortOrder = req.SortOrder
                        ?? (await db.Committees.Where(c => c.EventId == eventId).MaxAsync(c => (int?)c.SortOrder, ct) ?? 0) + 1;
        var committee = Committee.Create(eventId, name, Math.Min(sortOrder, Committee.MaxSortOrder), clock.GetUtcNow());
        db.Committees.Add(committee);
        audit.Record(me.Id, "committee.create", nameof(Committee), committee.Id.ToString(), after: CommitteeViews.Audit(committee));
        if (await SaveAsync(db, ct) is { } conflict) return conflict;
        await tx.CommitAsync(ct);
        return TypedResults.Created($"/api/events/{eventId}/committees/{committee.Id}",
            await CommitteeViews.CardAsync(db, openTasks, committee, canManage: true, ct));
    }

    private static async Task<IResult> Update(
        Guid eventId, Guid committeeId, CommitteeRequest req, HtnDbContext db, ICommitteeOpenTasks openTasks, IAuditLog audit,
        ICurrentUser me, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await EventLock.AcquireAsync(db, eventId, ct)) return TypedResults.NotFound();
        var committee = await FindAsync(db, eventId, committeeId, ct);
        if (committee is null) return TypedResults.NotFound();
        if (!committee.IsActive) return Inactive();
        var name = req.Name.Trim();
        if (await ExistsByNameAsync(db, eventId, name, committeeId, ct)) return Duplicate();

        var before = CommitteeViews.Audit(committee);
        committee.Update(name, req.SortOrder ?? committee.SortOrder);
        audit.Record(me.Id, "committee.update", nameof(Committee), committee.Id.ToString(), before, CommitteeViews.Audit(committee));
        if (await SaveAsync(db, ct) is { } conflict) return conflict;
        await tx.CommitAsync(ct);
        return TypedResults.Ok(await CommitteeViews.CardAsync(db, openTasks, committee, canManage: true, ct));
    }

    private static async Task<IResult> Deactivate(
        Guid eventId, Guid committeeId, CommitteeReasonRequest req, HtnDbContext db, ICommitteeOpenTasks openTasks, IAuditLog audit,
        ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await EventLock.AcquireAsync(db, eventId, ct)) return TypedResults.NotFound();
        var committee = await FindAsync(db, eventId, committeeId, ct);
        if (committee is null) return TypedResults.NotFound();
        if (!committee.IsActive) return Inactive();
        var open = await openTasks.ForCommitteeAsync(committeeId, ct);
        if (open > 0)
            return Problems.Result(StatusCodes.Status409Conflict, "This committee still has open tasks.",
                $"Finish, move or archive its {open} open tasks first.", CommitteeErrorCodes.HasOpenTasks);

        var now = clock.GetUtcNow();
        var before = CommitteeViews.Audit(committee);
        // Everyone leaves with the committee, so the pool and the one-committee-per-person index stay truthful.
        foreach (var member in await db.CommitteeMembers.Where(m => m.CommitteeId == committeeId && m.LeftAt == null).ToListAsync(ct))
            member.Leave(now);
        foreach (var volunteer in await db.EventVolunteers.Where(v => v.CommitteeId == committeeId).ToListAsync(ct))
            volunteer.AttachTo(null);
        committee.Deactivate(now);
        audit.Record(me.Id, "committee.deactivate", nameof(Committee), committee.Id.ToString(), before,
            CommitteeViews.Audit(committee), req.Reason.Trim());
        if (await SaveAsync(db, ct) is { } conflict) return conflict;
        await tx.CommitAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Sets or clears the lead. The lead must be an active goon with a login; the previous lead stays on as a plain
    /// member, and someone already on another committee of this event is refused.
    /// </summary>
    private static async Task<IResult> SetLead(
        Guid eventId, Guid committeeId, CommitteeLeadRequest req, HtnDbContext db, ICommitteeOpenTasks openTasks, IAuditLog audit,
        ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await EventLock.AcquireAsync(db, eventId, ct)) return TypedResults.NotFound();
        var committee = await FindAsync(db, eventId, committeeId, ct);
        if (committee is null) return TypedResults.NotFound();
        if (!committee.IsActive) return Inactive();

        var now = clock.GetUtcNow();
        var before = CommitteeViews.Audit(committee);
        var members = await db.CommitteeMembers.Where(m => m.EventId == eventId && m.LeftAt == null).ToListAsync(ct);

        if (req.PersonId is { } personId)
        {
            if (!await IsLevelAsync(db, personId, [PersonLevel.Goon], ct))
                return Problems.Field("personId", "A committee lead must be an active goon.", CommitteeErrorCodes.WrongLevel);
            var elsewhere = members.FirstOrDefault(m => m.PersonId == personId && m.CommitteeId != committeeId);
            if (elsewhere is not null)
                return elsewhere.Role == CommitteeRole.Lead
                    ? Conflict("That goon already leads another committee of this event.", CommitteeErrorCodes.AlreadyLead)
                    : Conflict("That goon is already on another committee of this event.", CommitteeErrorCodes.AlreadyInCommittee);

            // The outgoing lead keeps their seat as a plain member.
            foreach (var old in members.Where(m => m.CommitteeId == committeeId && m.Role == CommitteeRole.Lead && m.PersonId != personId))
                old.ChangeRole(CommitteeRole.Member);
            var existing = members.FirstOrDefault(m => m.CommitteeId == committeeId && m.PersonId == personId);
            if (existing is null) db.CommitteeMembers.Add(CommitteeMember.Join(committee, personId, CommitteeRole.Lead, now));
            else existing.ChangeRole(CommitteeRole.Lead);
            committee.SetLead(personId);
        }
        else
        {
            foreach (var old in members.Where(m => m.CommitteeId == committeeId && m.Role == CommitteeRole.Lead))
                old.ChangeRole(CommitteeRole.Member);
            committee.SetLead(null);
        }

        audit.Record(me.Id, "committee.set_lead", nameof(Committee), committee.Id.ToString(), before, CommitteeViews.Audit(committee));
        if (await SaveAsync(db, ct) is { } conflict) return conflict;
        await tx.CommitAsync(ct);
        return TypedResults.Ok(await CommitteeViews.CardAsync(db, openTasks, committee, canManage: true, ct));
    }

    // ---------- Members (board or that committee's lead) ----------

    private static async Task<IResult> AddMember(
        Guid eventId, Guid committeeId, CommitteeMemberRequest req, HttpContext http, IAuthorizationService auth, HtnDbContext db,
        ICommitteeOpenTasks openTasks, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await EventLock.AcquireAsync(db, eventId, ct)) return TypedResults.NotFound();
        var committee = await FindAsync(db, eventId, committeeId, ct);
        if (committee is null) return TypedResults.NotFound();
        if (await auth.DenyAsync(http, new CommitteeScope(committeeId), Policies.CommitteesManageMembers) is { } denied) return denied;
        if (!committee.IsActive) return Inactive();

        var personId = req.PersonId!.Value;
        if (!await IsLevelAsync(db, personId, CommitteeViews.MemberLevels, ct))
            return Problems.Field("personId", "Only an active board member, goon or apprentice with a login can join a committee.",
                CommitteeErrorCodes.WrongLevel);

        var active = await db.CommitteeMembers.Where(m => m.EventId == eventId && m.PersonId == personId && m.LeftAt == null)
            .ToListAsync(ct);
        if (active.Any(m => m.CommitteeId == committeeId))
            return TypedResults.Ok(await CommitteeViews.CardAsync(db, openTasks, committee, canManage: true, ct)); // idempotent
        if (active.Count > 0)
            return Conflict("That person is already on another committee of this event.", CommitteeErrorCodes.AlreadyInCommittee);

        var member = CommitteeMember.Join(committee, personId, CommitteeRole.Member, clock.GetUtcNow());
        db.CommitteeMembers.Add(member);
        audit.Record(me.Id, "committee.add_member", nameof(CommitteeMember), member.Id.ToString(), after: CommitteeViews.Audit(member));
        if (await SaveAsync(db, ct) is { } conflict) return conflict;
        await tx.CommitAsync(ct);
        return TypedResults.Ok(await CommitteeViews.CardAsync(db, openTasks, committee, canManage: true, ct));
    }

    /// <summary>Records the person leaving. History is kept; the row is never deleted. The lead leaves through PUT /lead.</summary>
    private static async Task<IResult> RemoveMember(
        Guid eventId, Guid committeeId, Guid personId, HttpContext http, IAuthorizationService auth, HtnDbContext db, IAuditLog audit,
        ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await EventLock.AcquireAsync(db, eventId, ct)) return TypedResults.NotFound();
        var committee = await FindAsync(db, eventId, committeeId, ct);
        if (committee is null) return TypedResults.NotFound();
        if (await auth.DenyAsync(http, new CommitteeScope(committeeId), Policies.CommitteesManageMembers) is { } denied) return denied;

        var member = await db.CommitteeMembers
            .SingleOrDefaultAsync(m => m.CommitteeId == committeeId && m.PersonId == personId && m.LeftAt == null, ct);
        if (member is null) return TypedResults.NotFound();
        if (member.Role == CommitteeRole.Lead || committee.LeadPersonId == personId)
            return Conflict("Change the committee lead first.", CommitteeErrorCodes.IsLead);

        var before = CommitteeViews.Audit(member);
        member.Leave(clock.GetUtcNow());
        audit.Record(me.Id, "committee.remove_member", nameof(CommitteeMember), member.Id.ToString(), before,
            CommitteeViews.Audit(member));
        if (await SaveAsync(db, ct) is { } conflict) return conflict;
        await tx.CommitAsync(ct);
        return TypedResults.NoContent();
    }

    // ---------- Volunteers on a committee ----------

    private static async Task<IResult> AttachVolunteer(
        Guid eventId, Guid committeeId, Guid volunteerId, HttpContext http, IAuthorizationService auth, HtnDbContext db,
        ICommitteeOpenTasks openTasks, IAuditLog audit, ICurrentUser me, CancellationToken ct)
    {
        var committee = await FindAsync(db, eventId, committeeId, ct);
        if (committee is null) return TypedResults.NotFound();
        if (await auth.DenyAsync(http, new CommitteeScope(committeeId), Policies.CommitteesManageMembers) is { } denied) return denied;
        if (!committee.IsActive) return Inactive();
        var volunteer = await db.EventVolunteers.SingleOrDefaultAsync(v => v.Id == volunteerId && v.EventId == eventId, ct);
        if (volunteer is null) return TypedResults.NotFound();
        if (!volunteer.IsActive)
            return Conflict("That volunteer is deactivated.", CommitteeErrorCodes.VolunteerInactive);
        // Moving a volunteer off another committee needs the right on that committee too.
        if (volunteer.CommitteeId is { } from && from != committeeId
            && await auth.DenyAsync(http, new CommitteeScope(from), Policies.CommitteesManageMembers) is { } fromDenied)
            return fromDenied;

        var before = CommitteeViews.Audit(volunteer);
        if (volunteer.AttachTo(committee))
        {
            audit.Record(me.Id, "volunteer.attach", nameof(EventVolunteer), volunteer.Id.ToString(), before,
                CommitteeViews.Audit(volunteer));
            if (await SaveAsync(db, ct) is { } conflict) return conflict;
        }
        return TypedResults.Ok(await CommitteeViews.CardAsync(db, openTasks, committee, canManage: true, ct));
    }

    private static async Task<IResult> DetachVolunteer(
        Guid eventId, Guid committeeId, Guid volunteerId, HttpContext http, IAuthorizationService auth, HtnDbContext db,
        IAuditLog audit, ICurrentUser me, CancellationToken ct)
    {
        var committee = await FindAsync(db, eventId, committeeId, ct);
        if (committee is null) return TypedResults.NotFound();
        if (await auth.DenyAsync(http, new CommitteeScope(committeeId), Policies.CommitteesManageMembers) is { } denied) return denied;
        var volunteer = await db.EventVolunteers
            .SingleOrDefaultAsync(v => v.Id == volunteerId && v.EventId == eventId && v.CommitteeId == committeeId, ct);
        if (volunteer is null) return TypedResults.NotFound();

        var before = CommitteeViews.Audit(volunteer);
        volunteer.AttachTo(null);
        audit.Record(me.Id, "volunteer.detach", nameof(EventVolunteer), volunteer.Id.ToString(), before, CommitteeViews.Audit(volunteer));
        if (await SaveAsync(db, ct) is { } conflict) return conflict;
        return TypedResults.NoContent();
    }

    // ---------- Shared ----------

    internal static Task<Committee?> FindAsync(HtnDbContext db, Guid eventId, Guid committeeId, CancellationToken ct) =>
        db.Committees.SingleOrDefaultAsync(c => c.Id == committeeId && c.EventId == eventId, ct);

    private static Task<bool> ExistsByNameAsync(HtnDbContext db, Guid eventId, string name, Guid? exceptId, CancellationToken ct) =>
        db.Committees.AnyAsync(c => c.EventId == eventId && c.Id != exceptId && c.Name.ToLower() == name.ToLower(), ct);

    /// <summary>The person is on the roster, active, holds one of the levels and has an active login.</summary>
    internal static Task<bool> IsLevelAsync(HtnDbContext db, Guid personId, PersonLevel[] levels, CancellationToken ct) =>
        db.People.AnyAsync(p => p.Id == personId && p.IsActive && levels.Contains(p.Level)
                                && db.Users.Any(u => u.PersonId == p.Id && u.IsActive), ct);

    /// <summary>Turns the race that slips past the pre-checks (unique indexes) into the same 409 the check returns.</summary>
    internal static async Task<IResult?> SaveAsync(HtnDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException ex) when (EventViews.IsUniqueViolation(ex, CommitteeNameIndex))
        {
            return Duplicate();
        }
        catch (DbUpdateException ex) when (EventViews.IsUniqueViolation(ex, ActivePersonIndex))
        {
            return Conflict("That person is already on another committee of this event.", CommitteeErrorCodes.AlreadyInCommittee);
        }
        catch (DbUpdateException ex) when (EventViews.IsUniqueViolation(ex, ActiveLeadIndex))
        {
            return Conflict("That committee already has a lead.", CommitteeErrorCodes.AlreadyLead);
        }
    }

    private const string CommitteeNameIndex = "ux_committees_event_name";
    private const string ActivePersonIndex = "ux_committee_members_active_person";
    private const string ActiveLeadIndex = "ux_committee_members_active_lead";

    internal static IResult Conflict(string title, string code) =>
        Problems.Result(StatusCodes.Status409Conflict, title, code: code);

    private static IResult Duplicate() =>
        Conflict("A committee of this event already has that name.", CommitteeErrorCodes.DuplicateName);

    private static IResult Inactive() =>
        Conflict("This committee is deactivated.", CommitteeErrorCodes.CommitteeInactive);
}
