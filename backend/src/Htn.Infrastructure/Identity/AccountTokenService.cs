using System.Security.Cryptography;
using System.Text;
using Htn.Domain.Common;
using Htn.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Htn.Infrastructure.Identity;

public sealed class AccountTokenOptions
{
    public const string Section = "Auth:Tokens";
    public TimeSpan InviteLifetime { get; set; } = TimeSpan.FromHours(48);
    public TimeSpan PasswordResetLifetime { get; set; } = TimeSpan.FromMinutes(30);
}

public interface IAccountTokenService
{
    /// <summary>Revokes any open tokens of the same purpose for the user and adds a new one (caller saves). Returns the raw token.</summary>
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
    public static string Hash(string rawToken) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    public async Task<string> IssueAsync(Guid userId, AccountTokenPurpose purpose, Guid? issuedById, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var open = await db.AccountTokens
            .Where(t => t.UserId == userId && t.Purpose == purpose && t.UsedAt == null && t.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var t in open) t.Revoke(now);

        var raw = ids.NewToken(32);
        var lifetime = purpose == AccountTokenPurpose.Invite ? options.Value.InviteLifetime : options.Value.PasswordResetLifetime;
        db.AccountTokens.Add(new AccountToken(userId, purpose, Hash(raw), now, now + lifetime, issuedById));
        return raw;
    }

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
