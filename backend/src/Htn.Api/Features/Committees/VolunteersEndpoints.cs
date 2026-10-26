using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Domain.Committees;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Committees;

/// <summary>
/// Event volunteers (frame 06, "+ Add volunteer (no login)"). No login, no points, never on the leaderboard; a
/// volunteer belongs to one event and optionally to one of its committees. Nothing is deleted: a volunteer is
/// deactivated with a reason.
/// <para>
/// Scope, like the committee routes: the route carries <c>Committees.ManageMembers</c> as the coarse gate, and the
/// handler re-runs it against the volunteer's committee (<see cref="CommitteeScope"/>) because that is only known
/// after the row is read. A lead may only work with volunteers on their own committee; an unattached volunteer is
/// board-only.
/// </para>
/// </summary>
public static class VolunteersEndpoints
{
    public static RouteGroupBuilder MapEventVolunteers(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/events/{eventId:guid}/volunteers").WithTags("Committees");
        g.MapPost("/", Create).RequireAuthorization(Policies.CommitteesManageMembers).Validate<VolunteerRequest>();
        g.MapPut("/{volunteerId:guid}", Update).RequireAuthorization(Policies.CommitteesManageMembers).Validate<VolunteerRequest>();
        g.MapPost("/{volunteerId:guid}/deactivate", Deactivate).RequireAuthorization(Policies.CommitteesManageMembers)
            .Validate<CommitteeReasonRequest>();
        return g;
    }

    private static async Task<IResult> Create(
        Guid eventId, VolunteerRequest req, HttpContext http, IAuthorizationService auth, HtnDbContext db, IAuditLog audit,
        ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, ct)) return TypedResults.NotFound();
        var target = await TargetAsync(db, eventId, req.CommitteeId, ct);
        if (target is TargetMissing) return TypedResults.NotFound();
        if (await DenyAsync(http, auth, req.CommitteeId) is { } denied) return denied;
        if (target is TargetInactive) return CommitteesEndpoints.Conflict("This committee is deactivated.", CommitteeErrorCodes.CommitteeInactive);

        var volunteer = EventVolunteer.Create(eventId, req.Name, req.Email, req.Mobile, clock.GetUtcNow());
        if (target is TargetFound found) volunteer.AttachTo(found.Committee);
        db.EventVolunteers.Add(volunteer);
        audit.Record(me.Id, "volunteer.create", nameof(EventVolunteer), volunteer.Id.ToString(), after: CommitteeViews.Audit(volunteer));
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/events/{eventId}/volunteers/{volunteer.Id}",
            CommitteeViews.Volunteer(volunteer, canManage: true));
    }

    private static async Task<IResult> Update(
        Guid eventId, Guid volunteerId, VolunteerRequest req, HttpContext http, IAuthorizationService auth, HtnDbContext db,
        IAuditLog audit, ICurrentUser me, CancellationToken ct)
    {
        var volunteer = await db.EventVolunteers.SingleOrDefaultAsync(v => v.Id == volunteerId && v.EventId == eventId, ct);
        if (volunteer is null) return TypedResults.NotFound();
        var target = await TargetAsync(db, eventId, req.CommitteeId, ct);
        if (target is TargetMissing) return TypedResults.NotFound();
        // Both ends: the committee the volunteer is on now, and the one they are being moved to.
        if (await DenyAsync(http, auth, volunteer.CommitteeId) is { } deniedFrom) return deniedFrom;
        if (await DenyAsync(http, auth, req.CommitteeId) is { } deniedTo) return deniedTo;
        if (!volunteer.IsActive)
            return CommitteesEndpoints.Conflict("That volunteer is deactivated.", CommitteeErrorCodes.VolunteerInactive);
        if (target is TargetInactive) return CommitteesEndpoints.Conflict("This committee is deactivated.", CommitteeErrorCodes.CommitteeInactive);

        var before = CommitteeViews.Audit(volunteer);
        var changed = volunteer.Update(req.Name, req.Email, req.Mobile);
        changed |= volunteer.AttachTo((target as TargetFound)?.Committee);
        if (changed)
        {
            audit.Record(me.Id, "volunteer.update", nameof(EventVolunteer), volunteer.Id.ToString(), before,
                CommitteeViews.Audit(volunteer));
            await db.SaveChangesAsync(ct);
        }
        return TypedResults.Ok(CommitteeViews.Volunteer(volunteer, canManage: true));
    }

    private static async Task<IResult> Deactivate(
        Guid eventId, Guid volunteerId, CommitteeReasonRequest req, HttpContext http, IAuthorizationService auth, HtnDbContext db,
        IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var volunteer = await db.EventVolunteers.SingleOrDefaultAsync(v => v.Id == volunteerId && v.EventId == eventId, ct);
        if (volunteer is null) return TypedResults.NotFound();
        if (await DenyAsync(http, auth, volunteer.CommitteeId) is { } denied) return denied;
        if (!volunteer.IsActive)
            return CommitteesEndpoints.Conflict("That volunteer is deactivated.", CommitteeErrorCodes.VolunteerInactive);

        var before = CommitteeViews.Audit(volunteer);
        volunteer.Deactivate(clock.GetUtcNow());
        audit.Record(me.Id, "volunteer.deactivate", nameof(EventVolunteer), volunteer.Id.ToString(), before,
            CommitteeViews.Audit(volunteer), req.Reason.Trim());
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    // ---------- Scope ----------

    /// <summary>Board, or the lead of that committee. An unattached volunteer (no committee) is board-only.</summary>
    private static async Task<IResult?> DenyAsync(HttpContext http, IAuthorizationService auth, Guid? committeeId)
    {
        if (http.User.IsInRole(Roles.Board)) return null;
        if (committeeId is not { } id)
            return Problems.Result(StatusCodes.Status403Forbidden, "You don't have access to this.",
                "Only the board can manage a volunteer who is not on a committee.", ScopedAuthorization.ForbiddenCode);
        return await auth.DenyAsync(http, new CommitteeScope(id), Policies.CommitteesManageMembers);
    }

    private abstract record Target;

    private sealed record TargetNone : Target;

    private sealed record TargetMissing : Target;

    private sealed record TargetInactive(Committee Committee) : Target;

    private sealed record TargetFound(Committee Committee) : Target;

    private static async Task<Target> TargetAsync(HtnDbContext db, Guid eventId, Guid? committeeId, CancellationToken ct)
    {
        if (committeeId is not { } id) return new TargetNone();
        var committee = await CommitteesEndpoints.FindAsync(db, eventId, id, ct);
        return committee is null ? new TargetMissing() : committee.IsActive ? new TargetFound(committee) : new TargetInactive(committee);
    }
}
