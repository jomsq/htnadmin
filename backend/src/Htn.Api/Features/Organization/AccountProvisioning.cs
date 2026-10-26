using Htn.Api.Common;
using Htn.Domain.Organization;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Email;
using Htn.Infrastructure.Identity;
using Htn.Infrastructure.Outbox;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Organization;

public sealed record LoginProvisionResult(AppUser? User, bool Invited, IReadOnlyList<string> Errors)
{
    public bool Succeeded => Errors.Count == 0;
}

/// <summary>
/// The one path that gives a person a login: create (or re-enable) the <see cref="AppUser"/>, keep its Identity role in
/// sync with the person's level, and queue a single-use invite through <see cref="IAccountTokenService"/> when the
/// account has no password yet. Used by create person, CSV import and promotions. Callers open a transaction first:
/// UserManager saves every pending change in the context.
/// </summary>
public sealed class AccountProvisioning(HtnDbContext db, UserManager<AppUser> users, IAccountTokenService tokens, TimeProvider clock)
{
    private static readonly string[] LoginRoles = [Roles.Board, Roles.Goon, Roles.Apprentice];

    public async Task<LoginProvisionResult> EnsureLoginAsync(Person person, Guid? actorId, string? inviterName, CancellationToken ct)
    {
        if (!person.Level.HasLogin()) throw new InvalidOperationException("Members have no login.");
        var now = clock.GetUtcNow();
        var user = await db.Users.SingleOrDefaultAsync(u => u.PersonId == person.Id, ct);
        if (user is null)
        {
            user = new AppUser
            {
                Id = Guid.CreateVersion7(now),
                UserName = person.Email,
                Email = person.Email,
                PersonId = person.Id,
                CreatedAt = now,
                LockoutEnabled = true,
            };
            var created = await users.CreateAsync(user); // no password: set on invite accept
            if (!created.Succeeded) return new LoginProvisionResult(null, false, created.Errors.Select(e => e.Description).ToArray());
        }
        else if (!user.IsActive)
        {
            user.IsActive = true;
            await users.UpdateAsync(user);
        }

        await SyncRoleAsync(user, person.Level);

        var invited = false;
        if (user.PasswordHash is null)
        {
            var raw = await tokens.IssueAsync(user.Id, AccountTokenPurpose.Invite, actorId, ct); // revokes any older link
            db.Outbox.Add(OutboxProcessor.InviteEmail(
                new InviteEmailPayload(user.Id, user.Email!, person.FirstName, inviterName, person.Level.ToString(), person.Position),
                raw, now));
            invited = true;
        }
        return new LoginProvisionResult(user, invited, []);
    }

    /// <summary>For a move to Member (roster only): removes roles, disables the login and revokes open links.</summary>
    public async Task DisableLoginAsync(AppUser user, CancellationToken ct)
    {
        user.IsActive = false;
        user.IsCfo = false;
        user.IsFinanceAssistant = false;
        await tokens.RevokeAllAsync(user.Id, ct);
        await SyncRoleAsync(user, null);
        await users.UpdateSecurityStampAsync(user); // ends existing sessions at the next stamp check
    }

    private async Task SyncRoleAsync(AppUser user, PersonLevel? level)
    {
        var wanted = level is { } l && l.HasLogin() ? Roles.For(l) : null;
        var current = await users.GetRolesAsync(user);
        var remove = current.Where(r => LoginRoles.Contains(r) && r != wanted).ToArray();
        if (remove.Length > 0)
        {
            var removed = await users.RemoveFromRolesAsync(user, remove);
            if (!removed.Succeeded) throw new InvalidOperationException("Could not remove role: " + string.Join("; ", removed.Errors.Select(e => e.Code)));
        }
        if (wanted is not null && !current.Contains(wanted))
        {
            var added = await users.AddToRoleAsync(user, wanted);
            if (!added.Succeeded) throw new InvalidOperationException("Could not assign role: " + string.Join("; ", added.Errors.Select(e => e.Code)));
        }
    }

    public static async Task<string?> ActorNameAsync(HtnDbContext db, ICurrentUser me, CancellationToken ct)
    {
        if (me.PersonId is not { } pid) return null;
        return await db.People.Where(p => p.Id == pid).Select(p => p.FirstName + " " + p.LastName).SingleOrDefaultAsync(ct);
    }
}
