using FluentValidation;
using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Domain.Organization;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Htn.Api.Features.Organization;

public sealed record FinanceRolesDto(PersonRefDto? Cfo, PersonRefDto? Assistant);

public sealed record UpdateFinanceRolesRequest(Guid? CfoPersonId, Guid? AssistantPersonId);

public sealed class UpdateFinanceRolesRequestValidator : AbstractValidator<UpdateFinanceRolesRequest>
{
    public UpdateFinanceRolesRequestValidator()
    {
        // There is always a CFO: it can be handed to someone else here, never cleared.
        RuleFor(x => x.CfoPersonId).NotNull().WithMessage("Choose the CFO.").NotEqual(Guid.Empty).WithMessage("Choose the CFO.");
        RuleFor(x => x.AssistantPersonId).NotEqual(Guid.Empty).WithMessage("Choose a person.");
    }
}

/// <summary>
/// The CFO (one active Board member with a login) and the finance assistant (at most one active Goon with a login).
/// Both are flags on <see cref="AppUser"/>, unique in the database, and show up as claims after the next stamp check.
/// </summary>
public static class FinanceRolesEndpoints
{
    public static RouteGroupBuilder MapFinanceRoles(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/finance-roles").WithTags("Finance roles");
        g.MapGet("/", Get).RequireAuthorization(Policies.PeopleManage);
        g.MapPut("/", Update).RequireAuthorization(Policies.PeopleManage).Validate<UpdateFinanceRolesRequest>();
        return g;
    }

    private static async Task<FinanceRolesDto> Load(HtnDbContext db, CancellationToken ct)
    {
        var holders = await (
            from u in db.Users.AsNoTracking()
            where u.IsActive && (u.IsCfo || u.IsFinanceAssistant)
            join p in db.People on u.PersonId equals (Guid?)p.Id
            select new { u.IsCfo, Ref = new PersonRefDto(p.Id, p.FirstName, p.LastName, p.Level) })
            .ToListAsync(ct);
        return new FinanceRolesDto(
            holders.FirstOrDefault(h => h.IsCfo)?.Ref,
            holders.FirstOrDefault(h => !h.IsCfo)?.Ref);
    }

    private static async Task<IResult> Get(HtnDbContext db, CancellationToken ct) => TypedResults.Ok(await Load(db, ct));

    private static async Task<(AppUser? User, IResult? Error)> Candidate(
        HtnDbContext db, Guid personId, PersonLevel level, string role, CancellationToken ct)
    {
        var person = await db.People.AsNoTracking().SingleOrDefaultAsync(p => p.Id == personId, ct);
        if (person is null)
            return (null, Problems.Result(StatusCodes.Status400BadRequest, $"The {role} isn't on the roster.", code: "person_not_found"));
        if (!person.IsActive || person.Level != level)
            return (null, Problems.Result(StatusCodes.Status400BadRequest,
                $"The {role} must be an active {(level == PersonLevel.Board ? "Board member" : "Goon")}.", code: "wrong_level"));
        var user = await db.Users.SingleOrDefaultAsync(u => u.PersonId == personId && u.IsActive, ct);
        if (user is null)
            return (null, Problems.Result(StatusCodes.Status400BadRequest, $"The {role} needs a login.", code: "no_login"));
        return (user, null);
    }

    private static async Task<IResult> Update(
        UpdateFinanceRolesRequest req, HtnDbContext db, UserManager<AppUser> users, IAuditLog audit, ICurrentUser me, CancellationToken ct)
    {
        var before = await Load(db, ct);

        // Nobody appoints themselves: a caller may only keep a role they already hold.
        if (me.PersonId is { } self)
        {
            if (req.CfoPersonId == self && before.Cfo?.Id != self)
                return Problems.Result(StatusCodes.Status409Conflict, "You can't appoint yourself CFO. Ask another board member.", code: "self_change");
            if (req.AssistantPersonId == self && before.Assistant?.Id != self)
                return Problems.Result(StatusCodes.Status409Conflict, "You can't appoint yourself finance assistant.", code: "self_change");
        }

        AppUser? cfo = null, assistant = null;
        if (req.CfoPersonId is { } cfoId)
        {
            var (user, error) = await Candidate(db, cfoId, PersonLevel.Board, "CFO", ct);
            if (error is not null) return error;
            cfo = user;
        }
        if (req.AssistantPersonId is { } assistantId)
        {
            var (user, error) = await Candidate(db, assistantId, PersonLevel.Goon, "finance assistant", ct);
            if (error is not null) return error;
            assistant = user;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // Clear first, in its own statement batch, so the single-holder unique indexes never see two holders.
            var current = await db.Users.Where(u => u.IsCfo || u.IsFinanceAssistant).ToListAsync(ct);
            var lostRights = new List<AppUser>();
            foreach (var u in current)
            {
                var lost = false;
                if (u.IsCfo && u.Id != cfo?.Id) { u.IsCfo = false; lost = true; }
                if (u.IsFinanceAssistant && u.Id != assistant?.Id) { u.IsFinanceAssistant = false; lost = true; }
                if (lost) lostRights.Add(u);
            }
            await db.SaveChangesAsync(ct);
            // Sessions of whoever lost finance rights end at the next stamp check instead of keeping the old claims.
            foreach (var u in lostRights) await users.UpdateSecurityStampAsync(u);

            if (cfo is not null) cfo.IsCfo = true;
            if (assistant is not null) assistant.IsFinanceAssistant = true;
            await db.SaveChangesAsync(ct);

            var after = await Load(db, ct);
            audit.Record(me.Id, "finance_roles.update", "FinanceRoles", "org",
                new { cfoPersonId = before.Cfo?.Id, assistantPersonId = before.Assistant?.Id },
                new { cfoPersonId = after.Cfo?.Id, assistantPersonId = after.Assistant?.Id });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return TypedResults.Ok(after);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await tx.RollbackAsync(ct);
            return Problems.Result(StatusCodes.Status409Conflict, "Finance roles changed at the same time. Reload and try again.", code: "conflict");
        }
    }
}
