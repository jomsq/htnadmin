namespace Htn.Infrastructure.Identity;

public enum AccountTokenPurpose
{
    Invite,
    PasswordReset,
}

/// <summary>
/// Single-use link token (invite or password reset). Only the SHA-256 hash is stored;
/// the raw token exists in the emailed link only.
/// </summary>
public sealed class AccountToken
{
    private AccountToken() { }

    public AccountToken(Guid userId, AccountTokenPurpose purpose, string tokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt, Guid? issuedById)
    {
        Id = Guid.CreateVersion7(createdAt);
        UserId = userId;
        Purpose = purpose;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        IssuedById = issuedById;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public AccountTokenPurpose Purpose { get; private set; }

    /// <summary>Lower-case hex SHA-256 of the raw token.</summary>
    public string TokenHash { get; private set; } = default!;

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    /// <summary>User who caused the token (the inviting admin); null for self-service resets.</summary>
    public Guid? IssuedById { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsUsable(DateTimeOffset now) => UsedAt is null && RevokedAt is null && ExpiresAt > now;

    public void Revoke(DateTimeOffset at) => RevokedAt ??= at;
}
