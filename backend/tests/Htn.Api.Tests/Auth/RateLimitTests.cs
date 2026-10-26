using System.Net;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;

namespace Htn.Api.Tests.Auth;

/// <summary>Low per-IP limit (3 per window) for every auth policy; per-email limits stay high.</summary>
public sealed class IpRateLimitApiFactory : HtnApiFactory
{
    protected override IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>
    {
        ["RateLimiting:AuthPermitLimit"] = "3",
        ["RateLimiting:AuthWindowSeconds"] = "600",
    };
}

/// <summary>High per-IP limit; login 3 and forgot 2 per email.</summary>
public sealed class EmailRateLimitApiFactory : HtnApiFactory
{
    protected override IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>
    {
        ["RateLimiting:LoginPerEmailPermitLimit"] = "3",
        ["RateLimiting:LoginPerEmailWindowSeconds"] = "600",
        ["RateLimiting:ForgotPerEmailPermitLimit"] = "2",
        ["RateLimiting:ForgotPerEmailWindowSeconds"] = "600",
    };
}

public sealed class LoginIpRateLimitTests(IpRateLimitApiFactory factory) : IClassFixture<IpRateLimitApiFactory>
{
    [Fact]
    public async Task Path_casing_and_trailing_slash_share_one_bucket_and_other_policies_are_separate()
    {
        var client = factory.NewClient();
        var body = new { email = HtnApiFactory.UniqueEmail(), password = "wrong-password-123" };

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/auth/login", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/auth/LOGIN", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/auth/login/", body)).StatusCode);

        var limited = await client.PostAsync("/api/Auth/Login/", body);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("rate_limited", (await ApiClient.JsonAsync(limited)).GetProperty("code").GetString());

        // auth-forgot has its own bucket.
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync("/api/auth/password/forgot", new { email = HtnApiFactory.UniqueEmail() })).StatusCode);
    }
}

public sealed class InviteIpRateLimitTests(IpRateLimitApiFactory factory) : IClassFixture<IpRateLimitApiFactory>
{
    [Fact]
    public async Task Different_tokens_in_the_path_do_not_get_fresh_buckets()
    {
        var client = factory.NewClient();
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/auth/invite/guess-{i}")).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/api/auth/invite/guess-new")).StatusCode);
        // Accept shares the invite bucket.
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await client.PostAsync("/api/auth/invite/accept", new { token = "guess", password = "long-enough-password" })).StatusCode);
    }
}

public sealed class EmailRateLimitTests(EmailRateLimitApiFactory factory) : IClassFixture<EmailRateLimitApiFactory>
{
    [Fact]
    public async Task Login_is_limited_per_email_the_same_for_unknown_and_existing_accounts()
    {
        var unknown = HtnApiFactory.UniqueEmail("nobody");
        var existing = await factory.CreateUserAsync(PersonLevel.Goon);

        foreach (var email in new[] { unknown, existing.Email })
        {
            for (var i = 0; i < 3; i++)
                Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().LoginAsync(email, "wrong-password-123")).StatusCode);
            // Casing doesn't open a new bucket, and the right password doesn't skip the limit.
            var limited = await factory.NewClient().LoginAsync(email.ToUpperInvariant(), HtnApiFactory.Password);
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        }

        // Another email is unaffected.
        var other = await factory.CreateUserAsync(PersonLevel.Goon);
        await factory.NewClient().LoginOkAsync(other.Email, HtnApiFactory.Password);
    }

    [Fact]
    public async Task Forgot_password_is_limited_per_email_the_same_for_unknown_and_existing_accounts()
    {
        var unknown = HtnApiFactory.UniqueEmail("ghost");
        var existing = await factory.CreateUserAsync(PersonLevel.Apprentice);

        foreach (var email in new[] { unknown, existing.Email })
        {
            for (var i = 0; i < 2; i++)
                Assert.Equal(HttpStatusCode.Accepted, (await factory.NewClient().PostAsync("/api/auth/password/forgot", new { email })).StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests,
                (await factory.NewClient().PostAsync("/api/auth/password/forgot", new { email = email.ToUpperInvariant() })).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Accepted,
            (await factory.NewClient().PostAsync("/api/auth/password/forgot", new { email = HtnApiFactory.UniqueEmail() })).StatusCode);
    }
}
