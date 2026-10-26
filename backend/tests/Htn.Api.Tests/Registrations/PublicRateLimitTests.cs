using System.Net;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;
using static Htn.Api.Tests.Events.EventTestData;
using static Htn.Api.Tests.Registrations.RegistrationTestData;

namespace Htn.Api.Tests.Registrations;

/// <summary>3 per IP per window for each public policy; the per-email registration limit stays high.</summary>
public sealed class PublicIpRateLimitApiFactory : HtnApiFactory
{
    protected override IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>
    {
        ["RateLimiting:PublicRegisterPermitLimit"] = "3",
        ["RateLimiting:PublicVoucherPermitLimit"] = "3",
        ["RateLimiting:PublicProofPermitLimit"] = "3",
        ["RateLimiting:PublicWindowSeconds"] = "600",
    };
}

/// <summary>2 registrations per buyer email; per-IP limits stay high.</summary>
public sealed class RegisterEmailRateLimitApiFactory : HtnApiFactory
{
    protected override IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>
    {
        ["RateLimiting:RegisterPerEmailPermitLimit"] = "2",
        ["RateLimiting:RegisterPerEmailWindowSeconds"] = "600",
    };
}

public sealed class PublicIpRateLimitTests(PublicIpRateLimitApiFactory factory) : IClassFixture<PublicIpRateLimitApiFactory>
{
    [Fact]
    public async Task Each_public_policy_limits_per_ip_regardless_of_slug_or_token()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board);
        var anon = factory.NewClient();

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Created, (await RegisterAsync(anon, ev.Slug, Body(ev.TicketId, [Person()]))).StatusCode);
        var limited = await RegisterAsync(anon, "another-slug", Body(ev.TicketId, [Person()]));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("rate_limited", (await ApiClient.JsonAsync(limited)).Code());

        // Voucher checks and proof pages have their own buckets.
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.OK, (await anon.PostAsync($"/api/public/events/{ev.Slug}/voucher-check", new { code = $"GUESS{i}", ticketTypeId = ev.TicketId })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await anon.PostAsync($"/api/public/events/{ev.Slug}/voucher-check", new { code = "GUESS9", ticketTypeId = ev.TicketId })).StatusCode);

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/api/public/proof/guess-{i}")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await anon.GetAsync("/api/public/proof/guess-new")).StatusCode);

        // The public page itself still loads (separate, higher limit).
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync($"/api/public/events/{ev.Slug}")).StatusCode);
    }
}

public sealed class RegisterEmailRateLimitTests(RegisterEmailRateLimitApiFactory factory) : IClassFixture<RegisterEmailRateLimitApiFactory>
{
    [Fact]
    public async Task Registrations_are_limited_per_buyer_email_case_insensitively()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board);
        var email = HtnApiFactory.UniqueEmail("limited");
        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync(factory.NewClient(), ev.Slug, Body(ev.TicketId, [Person(email)]))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync(factory.NewClient(), ev.Slug, Body(ev.TicketId, [Person(email.ToUpperInvariant())]))).StatusCode);
        var third = await RegisterAsync(factory.NewClient(), ev.Slug, Body(ev.TicketId, [Person(" " + email + " ")]));
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        // Another email is unaffected.
        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync(factory.NewClient(), ev.Slug, Body(ev.TicketId, [Person()]))).StatusCode);
    }
}
