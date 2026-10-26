using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Domain.Common;
using Htn.Domain.Organization;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Email;
using Htn.Infrastructure.Identity;
using Htn.Infrastructure.Outbox;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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
        g.MapGet("/{id:guid}", Get).RequireAuthorization(Policies.PeopleView);
        g.MapPost("/", Create).RequireAuthorization(Policies.PeopleManage).Validate<CreatePersonRequest>();
        g.MapPut("/{id:guid}", Update).RequireAuthorization(Policies.PeopleManage).Validate<UpdatePersonRequest>();
        g.MapPost("/{id:guid}/promote", Promote).RequireAuthorization(Policies.PeopleManage).Validate<PromotePersonRequest>();
        g.MapPost("/import", Import).RequireAuthorization(Policies.PeopleManage);
        g.MapPost("/{id:guid}/resend-invite", ResendInvite).RequireAuthorization(Policies.PeopleManage);
        g.MapPost("/{id:guid}/deactivate", Deactivate).RequireAuthorization(Policies.PeopleManage).ValidateOptional<DeactivatePersonRequest>();
        g.MapPost("/{id:guid}/send-password-reset", SendPasswordReset).RequireAuthorization(Policies.PeopleManage);
        return g;
    }

    /// <summary>Full roster row, for Board callers (People.Manage).</summary>
    internal static IQueryable<PersonDto> Project(HtnDbContext db, IQueryable<Person> people) =>
        from p in people
        join u in db.Users on (Guid?)p.Id equals u.PersonId into us
        from u in us.DefaultIfEmpty()
        select new PersonDto(
            p.Id, p.FirstName, p.MiddleName, p.LastName, p.Email, p.Level, p.Position, p.IsActive,
            !p.IsActive ? AccountStatuses.Deactivated
                : u == null || !u.IsActive ? AccountStatuses.NoLogin
                : u.PasswordHash == null ? AccountStatuses.InvitePending
                : AccountStatuses.Active,
            u != null ? u.LastSignInAt : null,
            p.CreatedAt,
            u != null && u.IsCfo,
            u != null && u.IsFinanceAssistant);

    /// <summary>
    /// Data-minimized row for non-Board People.View callers: masked email, no account status, last sign-in or finance
    /// roles. The users table isn't read at all; the mask runs in the final (client-side) projection.
    /// </summary>
    private static IQueryable<PersonDto> ProjectLimited(HtnDbContext db, IQueryable<Person> people) =>
        from p in people
        select new PersonDto(
            p.Id, p.FirstName, p.MiddleName, p.LastName, EmailMask.Mask(p.Email), p.Level, p.Position, p.IsActive,
            null, null, p.CreatedAt, false, false);

    private static async Task<IResult> List(
        HttpContext http, HtnDbContext db, string? level, string? search, int? page, int? pageSize, CancellationToken ct)
    {
        var full = http.User.IsInRole(Roles.Board);
        var q = db.People.AsNoTracking();
        if (!full) q = q.Where(p => p.IsActive); // non-Board callers only see the active roster

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
            // Non-Board callers can't search by email: matching on it would reveal the masked address.
            q = q.Where(p =>
                EF.Functions.ILike(p.FirstName, pattern, "\\") ||
                EF.Functions.ILike(p.LastName, pattern, "\\") ||
                (full && EF.Functions.ILike(p.Email, pattern, "\\")) ||
                (p.Position != null && EF.Functions.ILike(p.Position, pattern, "\\")) ||
                EF.Functions.ILike(p.FirstName + " " + p.LastName, pattern, "\\"));
        }

        var size = Math.Clamp(pageSize ?? 25, 1, 100);
        var current = Math.Max(page ?? 1, 1);
        var total = await q.CountAsync(ct);
        var paged = q.OrderBy(p => p.LastName).ThenBy(p => p.FirstName).ThenBy(p => p.Id).Skip((current - 1) * size).Take(size);
        var items = await (full ? Project(db, paged) : ProjectLimited(db, paged)).ToListAsync(ct);

        return TypedResults.Ok(new PagedResult<PersonDto>(items, total, current, size));
    }

    private static async Task<IResult> Create(
        CreatePersonRequest req, HtnDbContext db, UserManager<AppUser> users, AccountProvisioning accounts,
        IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var email = Person.NormalizeEmail(req.Email);
        var level = req.Level!.Value;
        if (await db.People.AnyAsync(p => p.Email == email, ct) || await users.FindByEmailAsync(email) is not null)
            return Problems.Field("email", DuplicateEmail);
        if (MemberRule.RequiresCheckIn(level) && !await HasCheckedInAsync(db, email, ct))
            return NoCheckIn();

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
                var login = await accounts.EnsureLoginAsync(person, me.Id, await AccountProvisioning.ActorNameAsync(db, me, ct), ct);
                if (!login.Succeeded)
                {
                    await tx.RollbackAsync(ct);
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["email"] = [.. login.Errors] });
                }
                user = login.User;
            }

            audit.Record(me.Id, "person.create", nameof(Person), person.Id.ToString(), before: null, after: new
            {
                person.FirstName, person.MiddleName, person.LastName, person.Email, person.Level, person.Position,
                HasLogin = user is not null, UserId = user?.Id,
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
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
        var inviter = await AccountProvisioning.ActorNameAsync(db, me, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var raw = await tokens.IssueAsync(user.Id, AccountTokenPurpose.Invite, me.Id, ct); // revokes the previous link
        db.Outbox.Add(OutboxProcessor.InviteEmail(
            new InviteEmailPayload(user.Id, user.Email!, person.FirstName, inviter, person.Level.ToString(), person.Position),
            raw, now));
        audit.Record(me.Id, "person.invite_resent", nameof(Person), person.Id.ToString());
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> Deactivate(
        Guid id, [FromBody] DeactivatePersonRequest? req, HtnDbContext db, UserManager<AppUser> users, IAccountTokenService tokens, IAuditLog audit,
        ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var person = await db.People.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (person is null) return TypedResults.NotFound();
        if (person.Id == me.PersonId)
            return Problems.Result(StatusCodes.Status409Conflict, "You can't deactivate your own account.", code: "self_deactivate");
        if (!person.IsActive)
            return Problems.Result(StatusCodes.Status409Conflict, "This person is already deactivated.", code: "person_deactivated");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var reason = string.IsNullOrWhiteSpace(req?.Reason) ? null : req.Reason.Trim();
        var now = clock.GetUtcNow();
        person.Deactivate(now, me.Id, reason);
        var vacated = await PositionOps.VacateAsync(db, person, _ => true, now, me.Id, "Deactivated", ct);

        var user = await db.Users.SingleOrDefaultAsync(u => u.PersonId == id, ct);
        var wasCfo = user?.IsCfo == true;
        var wasAssistant = user?.IsFinanceAssistant == true;
        if (user is not null)
        {
            user.IsActive = false;
            user.IsCfo = false;
            user.IsFinanceAssistant = false;
            await tokens.RevokeAllAsync(user.Id, ct);
            await users.UpdateSecurityStampAsync(user); // ends existing sessions at the next stamp check
        }

        audit.Record(me.Id, "person.deactivate", nameof(Person), person.Id.ToString(),
            before: new { isActive = true, isCfo = wasCfo, isFinanceAssistant = wasAssistant },
            after: new { isActive = false, userId = user?.Id, isCfo = false, isFinanceAssistant = false, vacatedPositions = vacated }, reason: reason);
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

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var raw = await tokens.IssueAsync(user.Id, AccountTokenPurpose.PasswordReset, me.Id, ct);
        db.Outbox.Add(OutboxProcessor.PasswordResetEmail(
            new PasswordResetEmailPayload(user.Id, user.Email!, person.FirstName), raw, clock.GetUtcNow()));
        audit.Record(me.Id, "person.password_reset_sent", nameof(Person), person.Id.ToString(), after: new { userId = user.Id });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> Get(Guid id, HttpContext http, HtnDbContext db, CancellationToken ct)
    {
        var full = http.User.IsInRole(Roles.Board);
        var q = db.People.AsNoTracking().Where(p => p.Id == id);
        if (!full) q = q.Where(p => p.IsActive); // same visibility as the list
        var person = await (full ? Project(db, q) : ProjectLimited(db, q)).SingleOrDefaultAsync(ct);
        if (person is null) return TypedResults.NotFound();

        var positions = await db.Positions.AsNoTracking()
            .Where(p => p.HolderId == id)
            .OrderBy(p => p.Kind).ThenBy(p => p.SortOrder)
            .Select(p => new PersonPositionDto(p.Id, p.Title, p.Kind))
            .ToListAsync(ct);

        IReadOnlyList<PersonHistoryDto> history = [];
        IReadOnlyList<PersonAttendanceDto> attendance = [];
        if (full)
        {
            var email = await db.People.AsNoTracking().Where(p => p.Id == id).Select(p => p.Email).SingleAsync(ct);
            attendance = await (
                    from a in db.Attendees.AsNoTracking()
                    where a.CheckedInAt != null && a.Email.ToLower() == email
                    join e in db.Events on a.EventId equals e.Id
                    orderby a.CheckedInAt descending, a.Id
                    select new PersonAttendanceDto(e.Id, e.Name, a.CheckedInAt!.Value))
                .ToListAsync(ct);

            var rows = await (
                from h in db.PersonHistory.AsNoTracking()
                where h.PersonId == id
                join u in db.Users on h.ActorId equals (Guid?)u.Id into us
                from u in us.DefaultIfEmpty()
                join a in db.People on (u == null ? null : u.PersonId) equals (Guid?)a.Id into actors
                from a in actors.DefaultIfEmpty()
                orderby h.At descending, h.Id descending
                select new { h.At, h.Kind, h.FromValue, h.ToValue, h.Reason, Actor = a == null ? null : a.FirstName + " " + a.LastName })
                .ToListAsync(ct);
            history = rows.Select(r => new PersonHistoryDto(r.At, PersonHistoryKinds.ToApiName(r.Kind), r.FromValue, r.ToValue, r.Reason, r.Actor)).ToList();
        }

        return TypedResults.Ok(new PersonDetailDto(person, positions, history, attendance));
    }

    private static async Task<IResult> Update(
        Guid id, UpdatePersonRequest req, HtnDbContext db, UserManager<AppUser> users, IAccountTokenService tokens,
        IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var person = await db.People.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (person is null) return TypedResults.NotFound();
        if (!person.IsActive)
            return Problems.Result(StatusCodes.Status409Conflict, "This person is deactivated.", code: "person_deactivated");

        var email = Person.NormalizeEmail(req.Email);
        var user = await db.Users.SingleOrDefaultAsync(u => u.PersonId == id, ct);
        var emailChanges = email != person.Email;
        if (emailChanges)
        {
            if (user is { PasswordHash: not null })
                return Problems.Result(StatusCodes.Status409Conflict,
                    "The email can't change once the account is active.", code: "email_locked");
            var clash = await db.People.AnyAsync(p => p.Email == email && p.Id != id, ct)
                        || await users.FindByEmailAsync(email) is { } other && other.Id != user?.Id;
            if (clash) return Problems.Result(StatusCodes.Status409Conflict, DuplicateEmail, code: "duplicate_email");
        }

        var before = new { person.FirstName, person.MiddleName, person.LastName, person.Email, person.Position };
        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            person.Rename(req.FirstName, req.MiddleName, req.LastName);
            person.ChangePosition(req.Position, now, me.Id);
            var reinvited = false;
            if (emailChanges)
            {
                person.ChangeEmail(email);
                await db.SaveChangesAsync(ct);
                if (user is not null)
                {
                    // Pending invite: move the login to the new address and send a fresh link there (the old one is revoked).
                    var setEmail = await users.SetEmailAsync(user, email);
                    var setName = setEmail.Succeeded ? await users.SetUserNameAsync(user, email) : setEmail;
                    if (!setName.Succeeded)
                    {
                        await tx.RollbackAsync(ct);
                        return Problems.Result(StatusCodes.Status409Conflict, DuplicateEmail, code: "duplicate_email");
                    }
                    var raw = await tokens.IssueAsync(user.Id, AccountTokenPurpose.Invite, me.Id, ct);
                    db.Outbox.Add(OutboxProcessor.InviteEmail(
                        new InviteEmailPayload(user.Id, email, person.FirstName, await AccountProvisioning.ActorNameAsync(db, me, ct),
                            person.Level.ToString(), person.Position),
                        raw, now));
                    reinvited = true;
                }
            }

            audit.Record(me.Id, "person.update", nameof(Person), person.Id.ToString(), before,
                new { person.FirstName, person.MiddleName, person.LastName, person.Email, person.Position, inviteResent = reinvited });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            await tx.RollbackAsync(ct);
            return Problems.Result(StatusCodes.Status409Conflict, DuplicateEmail, code: "duplicate_email");
        }

        return TypedResults.Ok(await Project(db, db.People.AsNoTracking().Where(p => p.Id == id)).SingleAsync(ct));
    }

    private static async Task<IResult> Promote(
        Guid id, PromotePersonRequest req, HtnDbContext db, UserManager<AppUser> users, AccountProvisioning accounts, IAuditLog audit,
        ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var person = await db.People.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (person is null) return TypedResults.NotFound();
        if (person.Id == me.PersonId)
            return Problems.Result(StatusCodes.Status409Conflict, "You can't change your own level.", code: "self_change");

        var to = req.ToLevel!.Value;
        var from = person.Level;
        switch (person.CheckPromotion(to, req.Reason))
        {
            case PromotionError.None: break;
            case PromotionError.Inactive:
                return Problems.Result(StatusCodes.Status409Conflict, "This person is deactivated.", code: "person_deactivated");
            case PromotionError.ReasonRequired:
                return Problems.Field("reason", PromotionErrors.Message(PromotionError.ReasonRequired, from, to), "reason_required");
            case PromotionError.SameLevel:
                return Problems.Result(StatusCodes.Status400BadRequest, PromotionErrors.Message(PromotionError.SameLevel, from, to), code: "same_level");
            case var error:
                return Problems.Result(StatusCodes.Status400BadRequest, PromotionErrors.Message(error, from, to), code: "invalid_promotion");
        }
        if (MemberRule.RequiresCheckIn(to) && !await HasCheckedInAsync(db, person.Email, ct))
            return NoCheckIn();

        var now = clock.GetUtcNow();
        var reason = string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason.Trim();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var user = await db.Users.SingleOrDefaultAsync(u => u.PersonId == id, ct);
        var before = new { level = from, isCfo = user?.IsCfo == true, isFinanceAssistant = user?.IsFinanceAssistant == true, hasLogin = user is { IsActive: true } };

        var kind = person.Promote(to, now, me.Id, reason);
        var vacated = await PositionOps.VacateAsync(db, person, p => PromotionRules.RequiredLevel(p.Kind) != to, now, me.Id, $"Level changed to {to}", ct);

        if (user is not null)
        {
            // The CFO is a Board member and the finance assistant a Goon; a flag that no longer fits is cleared.
            if (to != PersonLevel.Board) user.IsCfo = false;
            if (to != PersonLevel.Goon) user.IsFinanceAssistant = false;
        }

        var invited = false;
        if (to.HasLogin())
        {
            var login = await accounts.EnsureLoginAsync(person, me.Id, await AccountProvisioning.ActorNameAsync(db, me, ct), ct);
            if (!login.Succeeded)
            {
                await tx.RollbackAsync(ct);
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["email"] = [.. login.Errors] });
            }
            user = login.User;
            invited = login.Invited;
            // Losing Board, CFO or finance-assistant rights ends existing sessions at the next stamp check.
            var lostRights = (from == PersonLevel.Board && to != PersonLevel.Board) || (before.isCfo && !user!.IsCfo)
                             || (before.isFinanceAssistant && !user!.IsFinanceAssistant);
            if (lostRights && !invited) await users.UpdateSecurityStampAsync(user!);
        }
        else if (user is { IsActive: true })
        {
            await accounts.DisableLoginAsync(user, ct);
        }

        audit.Record(me.Id, kind == LevelChangeKind.Demotion ? "person.demote" : "person.promote", nameof(Person), person.Id.ToString(),
            before,
            new
            {
                level = to,
                isCfo = user?.IsCfo == true,
                isFinanceAssistant = user?.IsFinanceAssistant == true,
                hasLogin = user is { IsActive: true },
                inviteQueued = invited,
                vacatedPositions = vacated,
            },
            reason);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return TypedResults.Ok(await Project(db, db.People.AsNoTracking().Where(p => p.Id == id)).SingleAsync(ct));
    }

    private static async Task<IResult> Import(
        HttpRequest request, bool? dryRun, HtnDbContext db, UserManager<AppUser> users, AccountProvisioning accounts,
        IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var (file, uploadError) = await Uploads.ReadSingleFileAsync(request, "file", PeopleImport.MaxBytes, "The CSV must be 1 MB or smaller.", ct);
        if (uploadError is not null) return uploadError;

        var (rows, fileError) = PeopleImport.Read(file!.Bytes);
        if (fileError is not null) return Problems.Field("file", fileError, "invalid_csv");

        // Already in the organization (roster or login).
        var emails = rows!.Where(r => r.Email.Length > 0).Select(r => r.Email).Distinct().ToList();
        var normalized = emails.Select(e => users.NormalizeEmail(e)).ToList();
        var onRoster = (await db.People.Where(p => emails.Contains(p.Email)).Select(p => p.Email).ToListAsync(ct)).ToHashSet();
        var withLogin = (await db.Users.Where(u => normalized.Contains(u.NormalizedEmail!)).Select(u => u.NormalizedEmail!).ToListAsync(ct)).ToHashSet();
        foreach (var row in rows!.Where(r => onRoster.Contains(r.Email) || withLogin.Contains(users.NormalizeEmail(r.Email))))
            row.Errors.Add("Email is already in the organization.");

        var invalid = rows!.Count(r => r.Errors.Count > 0);
        var result = new ImportResultDto(rows!.Select(r => r.ToDto()).ToList(), rows!.Count - invalid, invalid, 0);
        if (dryRun != false) return TypedResults.Ok(result);
        if (invalid > 0)
            return Problems.Result(StatusCodes.Status400BadRequest, "Fix the rows with errors, then import again.", code: "import_has_errors");

        var now = clock.GetUtcNow();
        var batchId = Guid.CreateVersion7(now);
        var inviter = await AccountProvisioning.ActorNameAsync(db, me, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var created = new List<Person>();
            foreach (var row in rows!)
            {
                // Exempt from the Member check-in rule (existing members are brought in as they are); history says so.
                var person = Person.Create(row.FirstName, row.MiddleName, row.LastName, row.Email, row.Level!.Value, row.Position, now, me.Id,
                    MemberRule.ImportedReason);
                db.People.Add(person);
                created.Add(person);
            }
            await db.SaveChangesAsync(ct);

            foreach (var person in created)
            {
                AppUser? user = null;
                if (person.Level.HasLogin())
                {
                    var login = await accounts.EnsureLoginAsync(person, me.Id, inviter, ct);
                    if (!login.Succeeded)
                    {
                        await tx.RollbackAsync(ct);
                        return Problems.Result(StatusCodes.Status409Conflict, DuplicateEmail, code: "duplicate_email");
                    }
                    user = login.User;
                }
                audit.Record(me.Id, "person.create", nameof(Person), person.Id.ToString(), before: null, after: new
                {
                    person.FirstName, person.MiddleName, person.LastName, person.Email, person.Level, person.Position,
                    HasLogin = user is not null, UserId = user?.Id, ImportId = batchId,
                });
            }

            audit.Record(me.Id, "people.import", "PeopleImport", batchId.ToString(), after: new
            {
                created = created.Count,
                members = created.Count(p => p.Level == PersonLevel.Member),
                apprentices = created.Count(p => p.Level == PersonLevel.Apprentice),
                goons = created.Count(p => p.Level == PersonLevel.Goon),
                fileName = file.FileName,
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return TypedResults.Ok(result with { Created = created.Count });
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            await tx.RollbackAsync(ct);
            return Problems.Result(StatusCodes.Status409Conflict, DuplicateEmail, code: "duplicate_email");
        }
    }

    /// <summary>An attendee with this email (case-insensitive) has checked in at any event. <paramref name="email"/> is normalized.</summary>
    internal static Task<bool> HasCheckedInAsync(HtnDbContext db, string email, CancellationToken ct) =>
        db.Attendees.AsNoTracking().AnyAsync(a => a.CheckedInAt != null && a.Email.ToLower() == email, ct);

    private static IResult NoCheckIn() =>
        Problems.Result(StatusCodes.Status400BadRequest, MemberRule.Message, code: MemberRule.ErrorCode);

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
