using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Domain.Organization;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Email;
using Htn.Infrastructure.Identity;
using Htn.Infrastructure.Outbox;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Htn.Api.Features.Organization;

public static class PeopleEndpoints
{
    private const string DuplicateEmail = "Someone with this email is already in the organization.";

    public static RouteGroupBuilder MapPeople(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/people").WithTags("People");
        g.MapGet("/", List).RequireAuthorization(Policies.PeopleView);
        g.MapPost("/", Create).RequireAuthorization(Policies.PeopleManage).Validate<CreatePersonRequest>();
        g.MapPost("/{id:guid}/resend-invite", ResendInvite).RequireAuthorization(Policies.PeopleManage);
        g.MapPost("/{id:guid}/deactivate", Deactivate).RequireAuthorization(Policies.PeopleManage);
        g.MapPost("/{id:guid}/send-password-reset", SendPasswordReset).RequireAuthorization(Policies.PeopleManage);
        return g;
    }

    private static IQueryable<PersonDto> Project(HtnDbContext db, IQueryable<Person> people) =>
        from p in people
        join u in db.Users on (Guid?)p.Id equals u.PersonId into us
        from u in us.DefaultIfEmpty()
        select new PersonDto(
            p.Id, p.FirstName, p.MiddleName, p.LastName, p.Email, p.Level, p.Position, p.IsActive,
            !p.IsActive ? AccountStatuses.Deactivated
                : u == null ? AccountStatuses.NoLogin
                : u.PasswordHash == null ? AccountStatuses.InvitePending
                : AccountStatuses.Active,
            u != null ? u.LastSignInAt : null,
            p.CreatedAt);

    private static async Task<IResult> List(
        HtnDbContext db, string? level, string? search, int? page, int? pageSize, CancellationToken ct)
    {
        var q = db.People.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(level))
        {
            if (!Enum.TryParse<PersonLevel>(level, ignoreCase: true, out var lvl) || !Enum.IsDefined(lvl))
                return Problems.Field("level", "Unknown level.");
            q = q.Where(p => p.Level == lvl);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            if (term.Length > 100) term = term[..100];
            var pattern = "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            q = q.Where(p =>
                EF.Functions.ILike(p.FirstName, pattern, "\\") ||
                EF.Functions.ILike(p.LastName, pattern, "\\") ||
                EF.Functions.ILike(p.Email, pattern, "\\") ||
                (p.Position != null && EF.Functions.ILike(p.Position, pattern, "\\")) ||
                EF.Functions.ILike(p.FirstName + " " + p.LastName, pattern, "\\"));
        }

        var size = Math.Clamp(pageSize ?? 25, 1, 100);
        var current = Math.Max(page ?? 1, 1);
        var total = await q.CountAsync(ct);
        var items = await Project(db, q.OrderBy(p => p.LastName).ThenBy(p => p.FirstName).ThenBy(p => p.Id)
                .Skip((current - 1) * size).Take(size))
            .ToListAsync(ct);

        return TypedResults.Ok(new PagedResult<PersonDto>(items, total, current, size));
    }

    private static async Task<IResult> Create(
        CreatePersonRequest req, HtnDbContext db, UserManager<AppUser> users, IAccountTokenService tokens,
        IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var email = Person.NormalizeEmail(req.Email);
        var level = req.Level!.Value;
        if (await db.People.AnyAsync(p => p.Email == email, ct) || await users.FindByEmailAsync(email) is not null)
            return Problems.Field("email", DuplicateEmail);

        var now = clock.GetUtcNow();
        var person = Person.Create(req.FirstName, req.MiddleName, req.LastName, email, level, req.Position, now, me.Id);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            db.People.Add(person);
            await db.SaveChangesAsync(ct);

            AppUser? user = null;
            if (level.HasLogin())
            {
                user = new AppUser
                {
                    Id = Guid.CreateVersion7(now),
                    UserName = email,
                    Email = email,
                    PersonId = person.Id,
                    CreatedAt = now,
                    LockoutEnabled = true,
                };
                var created = await users.CreateAsync(user); // no password: set on invite accept
                if (!created.Succeeded)
                {
                    await tx.RollbackAsync(ct);
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["email"] = created.Errors.Select(e => e.Description).ToArray(),
                    });
                }
                var role = await users.AddToRoleAsync(user, Roles.For(level));
                if (!role.Succeeded) throw new InvalidOperationException("Could not assign role: " + string.Join("; ", role.Errors.Select(e => e.Code)));

                var raw = await tokens.IssueAsync(user.Id, AccountTokenPurpose.Invite, me.Id, ct);
                db.Outbox.Add(OutboxProcessor.InviteEmail(
                    new InviteEmailPayload(user.Id, email, person.FirstName, await ActorName(db, me, ct), level.ToString(), person.Position),
                    raw, now));
            }

            audit.Record(me.Id, "person.create", nameof(Person), person.Id.ToString(), before: null, after: new
            {
                person.FirstName, person.MiddleName, person.LastName, person.Email, person.Level, person.Position,
                HasLogin = user is not null, UserId = user?.Id,
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await tx.RollbackAsync(ct);
            return Problems.Field("email", DuplicateEmail);
        }

        var dto = await Project(db, db.People.AsNoTracking().Where(p => p.Id == person.Id)).SingleAsync(ct);
        return TypedResults.Created($"/api/people/{person.Id}", dto);
    }

    private static async Task<IResult> ResendInvite(
        Guid id, HtnDbContext db, IAccountTokenService tokens, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var person = await db.People.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (person is null) return TypedResults.NotFound();
        if (!person.IsActive)
            return Problems.Result(StatusCodes.Status409Conflict, "This person is deactivated.", code: "person_deactivated");

        var user = await db.Users.SingleOrDefaultAsync(u => u.PersonId == id, ct);
        if (user is null)
            return Problems.Result(StatusCodes.Status409Conflict, "This person has no login.", code: "no_login");
        if (user.PasswordHash is not null)
            return Problems.Result(StatusCodes.Status409Conflict, "This account is already active.", code: "already_active");

        var now = clock.GetUtcNow();
        var raw = await tokens.IssueAsync(user.Id, AccountTokenPurpose.Invite, me.Id, ct); // revokes the previous link
        db.Outbox.Add(OutboxProcessor.InviteEmail(
            new InviteEmailPayload(user.Id, user.Email!, person.FirstName, await ActorName(db, me, ct), person.Level.ToString(), person.Position),
            raw, now));
        audit.Record(me.Id, "person.invite_resent", nameof(Person), person.Id.ToString());
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> Deactivate(
        Guid id, HtnDbContext db, UserManager<AppUser> users, IAccountTokenService tokens, IAuditLog audit,
        ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var person = await db.People.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (person is null) return TypedResults.NotFound();
        if (person.Id == me.PersonId)
            return Problems.Result(StatusCodes.Status409Conflict, "You can't deactivate your own account.", code: "self_deactivate");
        if (!person.IsActive)
            return Problems.Result(StatusCodes.Status409Conflict, "This person is already deactivated.", code: "person_deactivated");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        person.Deactivate(clock.GetUtcNow(), me.Id, reason: null);

        var user = await db.Users.SingleOrDefaultAsync(u => u.PersonId == id, ct);
        if (user is not null)
        {
            user.IsActive = false;
            await tokens.RevokeAllAsync(user.Id, ct);
            await users.UpdateSecurityStampAsync(user); // ends existing sessions at the next stamp check
        }

        audit.Record(me.Id, "person.deactivate", nameof(Person), person.Id.ToString(),
            before: new { isActive = true }, after: new { isActive = false, userId = user?.Id });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> SendPasswordReset(
        Guid id, HtnDbContext db, IAccountTokenService tokens, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var person = await db.People.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        if (person is null) return TypedResults.NotFound();
        if (!person.IsActive)
            return Problems.Result(StatusCodes.Status409Conflict, "This person is deactivated.", code: "person_deactivated");

        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.PersonId == id, ct);
        if (user is null)
            return Problems.Result(StatusCodes.Status409Conflict, "This person has no login.", code: "no_login");
        if (!user.IsActive || user.PasswordHash is null)
            return Problems.Result(StatusCodes.Status409Conflict, "This account isn't active yet. Resend the invite instead.", code: "not_active");

        var raw = await tokens.IssueAsync(user.Id, AccountTokenPurpose.PasswordReset, me.Id, ct);
        db.Outbox.Add(OutboxProcessor.PasswordResetEmail(
            new PasswordResetEmailPayload(user.Id, user.Email!, person.FirstName), raw, clock.GetUtcNow()));
        audit.Record(me.Id, "person.password_reset_sent", nameof(Person), person.Id.ToString(), after: new { userId = user.Id });
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<string?> ActorName(HtnDbContext db, ICurrentUser me, CancellationToken ct)
    {
        if (me.PersonId is not { } pid) return null;
        return await db.People.Where(p => p.Id == pid).Select(p => p.FirstName + " " + p.LastName).SingleOrDefaultAsync(ct);
    }
}
