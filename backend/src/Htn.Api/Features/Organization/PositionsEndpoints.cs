using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Domain.Organization;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Htn.Api.Features.Organization;

public static class PositionsEndpoints
{
    public static RouteGroupBuilder MapPositions(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/positions").WithTags("Positions");
        g.MapGet("/", List).RequireAuthorization(Policies.PeopleView);
        g.MapPost("/", Create).RequireAuthorization(Policies.PeopleManage).Validate<CreatePositionRequest>();
        g.MapPut("/{id:guid}", Update).RequireAuthorization(Policies.PeopleManage).Validate<UpdatePositionRequest>();
        g.MapPost("/{id:guid}/assign", Assign).RequireAuthorization(Policies.PeopleManage).Validate<AssignPositionRequest>();
        g.MapPost("/{id:guid}/vacate", Vacate).RequireAuthorization(Policies.PeopleManage).ValidateOptional<VacatePositionRequest>();
        return g;
    }

    private static IQueryable<PositionDto> Project(HtnDbContext db, IQueryable<Position> positions) =>
        from p in positions
        join h in db.People on p.HolderId equals (Guid?)h.Id into hs
        from h in hs.DefaultIfEmpty()
        orderby p.Kind, p.SortOrder, p.Title
        select new PositionDto(p.Id, p.Kind, p.Title, p.SortOrder, p.IsActive,
            h == null ? null : new PersonRefDto(h.Id, h.FirstName, h.LastName, h.Level));

    private static Task<PositionDto> One(HtnDbContext db, Guid id, CancellationToken ct) =>
        Project(db, db.Positions.AsNoTracking().Where(p => p.Id == id)).SingleAsync(ct);

    private static async Task<IResult> List(HtnDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await Project(db, db.Positions.AsNoTracking()).ToListAsync(ct));

    private static async Task<IResult> Create(
        CreatePositionRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var kind = req.Kind!.Value;
        var next = (await db.Positions.Where(p => p.Kind == kind).MaxAsync(p => (int?)p.SortOrder, ct) ?? 0) + 1;
        var position = Position.Create(kind, req.Title, next, clock.GetUtcNow());
        db.Positions.Add(position);
        audit.Record(me.Id, "position.create", nameof(Position), position.Id.ToString(),
            after: new { position.Kind, position.Title, position.SortOrder });
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/positions/{position.Id}", await One(db, position.Id, ct));
    }

    private static async Task<IResult> Update(
        Guid id, UpdatePositionRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, CancellationToken ct)
    {
        var position = await db.Positions.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (position is null) return TypedResults.NotFound();
        if (req.IsActive == false && position.HolderId is not null)
            return Problems.Result(StatusCodes.Status409Conflict, "Vacate the position before deactivating it.", code: "position_held");

        var before = new { position.Title, position.IsActive };
        position.Rename(req.Title);
        position.SetActive(req.IsActive!.Value);
        audit.Record(me.Id, "position.update", nameof(Position), position.Id.ToString(), before, new { position.Title, position.IsActive });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await One(db, id, ct));
    }

    private static async Task<IResult> Assign(
        Guid id, AssignPositionRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var position = await db.Positions.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (position is null) return TypedResults.NotFound();
        var person = await db.People.SingleOrDefaultAsync(p => p.Id == req.PersonId, ct);
        if (person is null) return Problems.Field("personId", "That person isn't on the roster.", "person_not_found");

        var holdsAnotherSeat = position.Kind == PositionKind.BoardSeat && await db.Positions.AnyAsync(
            p => p.Kind == PositionKind.BoardSeat && p.HolderId == person.Id && p.Id != position.Id, ct);
        switch (position.CheckAssign(person, holdsAnotherSeat))
        {
            case AssignError.None: break;
            case AssignError.PositionInactive:
                return Problems.Result(StatusCodes.Status409Conflict, Position.AssignErrorMessage(AssignError.PositionInactive), code: "position_inactive");
            case AssignError.AlreadyHolder:
                return Problems.Result(StatusCodes.Status409Conflict, Position.AssignErrorMessage(AssignError.AlreadyHolder), code: "already_holder");
            case AssignError.PersonInactive:
                return Problems.Result(StatusCodes.Status400BadRequest, Position.AssignErrorMessage(AssignError.PersonInactive), code: "person_inactive");
            case AssignError.WrongLevel:
                return Problems.Result(StatusCodes.Status400BadRequest,
                    position.Kind == PositionKind.BoardSeat ? "Only Board members can hold a board seat." : "Only Goons can hold a goon position.",
                    code: "wrong_level");
            case var error:
                return Problems.Result(StatusCodes.Status400BadRequest, Position.AssignErrorMessage(error), code: "already_holds_seat");
        }

        var now = clock.GetUtcNow();
        var previous = position.HolderId;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            if (previous is { } prevId)
            {
                var holder = await db.People.SingleAsync(p => p.Id == prevId, ct);
                position.Vacate(holder, await PositionOps.OpenTermAsync(db, position, ct), now, me.Id, "Reassigned");
                await db.SaveChangesAsync(ct); // close the old term before the new one opens (one open term per position)
            }
            db.PositionAssignments.Add(position.Assign(person, holdsAnotherSeat, now, me.Id));
            audit.Record(me.Id, "position.assign", nameof(Position), position.Id.ToString(),
                new { holderId = previous }, new { holderId = person.Id, position.Title });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await tx.RollbackAsync(ct);
            return Problems.Result(StatusCodes.Status409Conflict, "Someone else changed this position. Reload and try again.", code: "conflict");
        }
        return TypedResults.Ok(await One(db, id, ct));
    }

    private static async Task<IResult> Vacate(
        Guid id, [FromBody] VacatePositionRequest? req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var position = await db.Positions.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (position is null) return TypedResults.NotFound();
        if (position.HolderId is not { } holderId)
            return Problems.Result(StatusCodes.Status409Conflict, "This position is already vacant.", code: "already_vacant");

        var reason = string.IsNullOrWhiteSpace(req?.Reason) ? null : req.Reason.Trim();
        var holder = await db.People.SingleAsync(p => p.Id == holderId, ct);
        position.Vacate(holder, await PositionOps.OpenTermAsync(db, position, ct), clock.GetUtcNow(), me.Id, reason);
        audit.Record(me.Id, "position.vacate", nameof(Position), position.Id.ToString(),
            new { holderId }, new { holderId = (Guid?)null, position.Title }, reason);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await One(db, id, ct));
    }
}
