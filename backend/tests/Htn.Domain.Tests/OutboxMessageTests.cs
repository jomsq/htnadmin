using Htn.Domain.Outbox;

namespace Htn.Domain.Tests;

public sealed class OutboxMessageTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Sent_clears_secret()
    {
        var m = OutboxMessage.Create("email.invite", "{}", "raw-token", Now);
        m.MarkSent(Now, "abc");
        Assert.Equal(OutboxStatus.Sent, m.Status);
        Assert.Equal(1, m.Attempts);
        Assert.Equal("abc", m.ExternalId);
        Assert.Null(m.Secret);
    }

    [Fact]
    public void Fails_after_five_attempts_with_backoff()
    {
        var m = OutboxMessage.Create("email.invite", "{}", "raw-token", Now);
        for (var i = 1; i < OutboxMessage.MaxAttempts; i++)
        {
            m.MarkAttemptFailed("boom", Now);
            Assert.Equal(OutboxStatus.Pending, m.Status);
            Assert.True(m.NextAttemptAt > Now);
        }
        m.MarkAttemptFailed("boom", Now);
        Assert.Equal(OutboxStatus.Failed, m.Status);
        Assert.Equal(5, m.Attempts);
        Assert.Null(m.Secret);
        Assert.Throws<InvalidOperationException>(() => m.MarkSent(Now, null));
    }
}
