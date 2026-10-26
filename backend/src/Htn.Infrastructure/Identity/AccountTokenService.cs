using System.Security.Cryptography;
using System.Text;
using Htn.Domain.Common;
using Htn.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Htn.Infrastructure.Identity;

public sealed class AccountTokenOptions
{
    public const string Section = "Auth:Tokens";
    public TimeSpan InviteLifetime { get; set; } = TimeSpan.FromHours(48);
    public TimeSpan PasswordResetLifetime { get; set; } = TimeSpan.FromMinutes(30);
}

public interface IAccountTokenService
{
    /// <summary>
    /// Revokes any open tokens of the same purpose for the user and saves a new one. Runs in the caller's transaction
    /// when there is one (so it commits with the caller's outbox/audit rows), otherwise in its own. A concurrent issue
    /// for the same user and purpose hits the <c>ux_account_tokens_open</c> index and is retried once. Note that this
    /// saves every pending change in the context. Returns the raw token.
    /// </summary>
    Task<string> IssueAsync(Guid userId, AccountTokenPurpose purpose, Guid? issuedById, CancellationToken ct);

    /// <summary>Revokes all open tokens for the user (caller saves).</summary>
    Task RevokeAllAsync(Guid userId, CancellationToken ct);

    /// <summary>Finds a usable token by its raw value. Does not consume it.</summary>
    Task<AccountToken?> FindUsableAsync(string rawToken, AccountTokenPurpose purpose, CancellationToken ct);

    /// <summary>Atomically marks the token used. Returns false if it was already used, revoked or expired (race-safe).</summary>
    Task<bool> TryConsumeAsync(Guid tokenId, CancellationToken ct);
}

public sealed class AccountTokenService(HtnDbContext db, IPublicIdGenerator ids, TimeProvider clock, IOptions<AccountTokenOptions> options)
    : IAccountTokenService
{
    /// <summary>Partial unique index: one open token per (user, purpose).</summary>
    public const string OpenTokenIndex = "ux_account_tokens_open";

    public static string Hash(string rawToken) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    public async Task<string> IssueAsync(Guid userId, AccountTokenPurpose purpose, Guid? issuedById, CancellationToken ct)
    {
        const string savepoint = "account_token_issue";
        var raw = ids.NewToken(32);
        var lifetime = purpose == AccountTokenPurpose.Invite ? options.Value.InviteLifetime : options.Value.PasswordResetLifetime;

        IDbContextTransaction? own = null;
        var tx = db.Database.CurrentTransaction ?? (own = await db.Database.BeginTransactionAsync(ct));
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                var now = clock.GetUtcNow();
                var token = new AccountToken(userId, purpose, Hash(raw), now, now + lifetime, issuedById);
                await tx.CreateSavepointAsync(savepoint, ct);
                try
                {
                    await db.AccountTokens
                        .Where(t => t.UserId == userId && t.Purpose == purpose && t.UsedAt == null && t.RevokedAt == null)
                        .ExecuteUpdateAsync(u => u.SetProperty(t => t.RevokedAt, now), ct);
                    db.AccountTokens.Add(token);
                    await db.SaveChangesAsync(ct);
                    break;
                }
                catch (DbUpdateException ex) when (attempt == 1 && IsOpenTokenConflict(ex))
                {
                    // Another request issued a token for this user/purpose at the same time. Revoke it and try again.
                    db.Entry(token).State = EntityState.Detached;
                    await tx.RollbackToSavepointAsync(savepoint, ct);
                }
            }
            if (own is not null) await own.CommitAsync(ct);
        }
        finally
        {
            if (own is not null) await own.DisposeAsync();
        }
        return raw;
    }

    private static bool IsOpenTokenConflict(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: OpenTokenIndex };

    public async Task RevokeAllAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var open = await db.AccountTokens
            .Where(t => t.UserId == userId && t.UsedAt == null && t.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var t in open) t.Revoke(now);
    }

    public async Task<AccountToken?> FindUsableAsync(string rawToken, AccountTokenPurpose purpose, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken) || rawToken.Length > 200) return null;
        var hash = Hash(rawToken.Trim());
        var now = clock.GetUtcNow();
        return await db.AccountTokens.AsNoTracking().SingleOrDefaultAsync(
            t => t.TokenHash == hash && t.Purpose == purpose && t.UsedAt == null && t.RevokedAt == null && t.ExpiresAt > now, ct);
    }

    public async Task<bool> TryConsumeAsync(Guid tokenId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var rows = await db.AccountTokens
            .Where(t => t.Id == tokenId && t.UsedAt == null && t.RevokedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), ct);
        return rows == 1;
    }
}
