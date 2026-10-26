using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Htn.Api.Tests.Auth;

public sealed class AccountTokenServiceTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    private async Task<string> IssueAsync(Guid userId, AccountTokenPurpose purpose)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAccountTokenService>().IssueAsync(userId, purpose, null, CancellationToken.None);
    }

    private Task<int> OpenCountAsync(Guid userId, AccountTokenPurpose purpose) =>
        factory.DbAsync(db => db.AccountTokens.CountAsync(t => t.UserId == userId && t.Purpose == purpose && t.UsedAt == null && t.RevokedAt == null));

    [Fact]
    public async Task Issuing_twice_leaves_exactly_one_open_token()
    {
        var user = await factory.CreateUserAsync(PersonLevel.Goon);
        await IssueAsync(user.UserId!.Value, AccountTokenPurpose.PasswordReset);
        var second = await IssueAsync(user.UserId!.Value, AccountTokenPurpose.PasswordReset);

        Assert.Equal(1, await OpenCountAsync(user.UserId!.Value, AccountTokenPurpose.PasswordReset));
        var open = await factory.DbAsync(db => db.AccountTokens.SingleAsync(t => t.UserId == user.UserId && t.UsedAt == null && t.RevokedAt == null));
        Assert.Equal(AccountTokenService.Hash(second), open.TokenHash);
    }

    [Fact]
    public async Task Concurrent_issues_leave_exactly_one_open_token()
    {
        var user = await factory.CreateUserAsync(PersonLevel.Goon);
        for (var round = 0; round < 5; round++)
        {
            await Task.WhenAll(
                IssueAsync(user.UserId!.Value, AccountTokenPurpose.Invite),
                IssueAsync(user.UserId!.Value, AccountTokenPurpose.Invite));
            Assert.Equal(1, await OpenCountAsync(user.UserId!.Value, AccountTokenPurpose.Invite));
        }
    }

    [Fact]
    public async Task Different_purposes_each_keep_their_own_open_token()
    {
        var user = await factory.CreateUserAsync(PersonLevel.Goon);
        await IssueAsync(user.UserId!.Value, AccountTokenPurpose.Invite);
        await IssueAsync(user.UserId!.Value, AccountTokenPurpose.PasswordReset);
        Assert.Equal(1, await OpenCountAsync(user.UserId!.Value, AccountTokenPurpose.Invite));
        Assert.Equal(1, await OpenCountAsync(user.UserId!.Value, AccountTokenPurpose.PasswordReset));
    }

    [Fact]
    public async Task Database_rejects_a_second_open_token()
    {
        var user = await factory.CreateUserAsync(PersonLevel.Goon);
        await IssueAsync(user.UserId!.Value, AccountTokenPurpose.Invite);
        var now = DateTimeOffset.UtcNow;
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => factory.DbAsync(async db =>
        {
            db.AccountTokens.Add(new AccountToken(user.UserId!.Value, AccountTokenPurpose.Invite, AccountTokenService.Hash(Guid.NewGuid().ToString()), now, now.AddHours(1), null));
            return await db.SaveChangesAsync();
        }));
        var pg = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(AccountTokenService.OpenTokenIndex, pg.ConstraintName);
    }
}
