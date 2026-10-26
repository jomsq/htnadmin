using System.Net;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;
using static Htn.Api.Tests.Events.EventTestData;
using static Htn.Api.Tests.Registrations.RegistrationTestData;

namespace Htn.Api.Tests.Registrations;

/// <summary>3 voucher attempts and 3 public reads per IP; everything else high.</summary>
public sealed class VoucherBucketApiFactory : HtnApiFactory
{
    protected override IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>
    {
        ["RateLimiting:PublicVoucherPermitLimit"] = "3",
        ["RateLimiting:PublicReadPermitLimit"] = "3",
        ["RateLimiting:PublicWindowSeconds"] = "600",
    };
}

/// <summary>2 registrations per buyer email and 5 checkout emails per IP.</summary>
public sealed class CheckoutEmailLimitsApiFactory : HtnApiFactory
{
    protected override IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>
    {
        ["RateLimiting:RegisterPerEmailPermitLimit"] = "2",
        ["RateLimiting:RegisterPerEmailWindowSeconds"] = "600",
        ["RateLimiting:PublicEmailsPerIpPermitLimit"] = "5",
        ["RateLimiting:PublicEmailsPerIpWindowSeconds"] = "600",
    };
}

public sealed class VoucherBucketTests(VoucherBucketApiFactory factory) : IClassFixture<VoucherBucketApiFactory>
{
    [Fact]
    public async Task Voucher_checks_and_registrations_with_a_code_share_one_bucket_and_public_files_are_limited()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board);
        var anon = factory.NewClient();
        var check = $"/api/public/events/{ev.Slug}/voucher-check";

        Assert.Equal(HttpStatusCode.OK, (await anon.PostAsync(check, new { code = "GUESS1", ticketTypeId = ev.TicketId })).StatusCode);
        // A registration carrying a (wrong) code spends an attempt too.
        Assert.Equal("invalid_voucher", (await ApiClient.JsonAsync(await RegisterAsync(anon, ev.Slug, Body(ev.TicketId, [Person()], voucherCode: "GUESS2")))).Code());
        Assert.Equal(HttpStatusCode.OK, (await anon.PostAsync(check, new { code = "GUESS3", ticketTypeId = ev.TicketId })).StatusCode);
        var limited = await anon.PostAsync(check, new { code = "GUESS4", ticketTypeId = ev.TicketId });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("rate_limited", (await ApiClient.JsonAsync(limited)).Code());
        Assert.Equal(HttpStatusCode.TooManyRequests, (await RegisterAsync(anon, ev.Slug, Body(ev.TicketId, [Person()], voucherCode: "GUESS5"))).StatusCode);
        // Without a code the registration isn't a voucher attempt.
        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync(anon, ev.Slug, Body(ev.TicketId, [Person()]))).StatusCode);

        // Public files share the public read bucket (3 here).
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/api/public/files/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await anon.GetAsync($"/api/public/files/{Guid.NewGuid()}")).StatusCode);
    }
}

public sealed class CheckoutEmailLimitsTests(CheckoutEmailLimitsApiFactory factory) : IClassFixture<CheckoutEmailLimitsApiFactory>
{
    [Fact]
    public async Task Email_limits_count_only_real_attempts_and_cap_emails_per_ip()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board, ticket: Ticket("Pair", 2000m, 10, groupSize: 2));
        var anon = factory.NewClient();
        var email = HtnApiFactory.UniqueEmail("limited");

        // Invalid bodies and unknown events don't spend the per-email (or per-IP) budget.
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await RegisterAsync(anon, ev.Slug, Body(ev.TicketId, [Person(email)], consent: false))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await RegisterAsync(anon, "no-such-event", Body(ev.TicketId, [Person(email), Member()]))).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync(anon, ev.Slug, Body(ev.TicketId, [Person(email), Member()]))).StatusCode); // 2 emails
        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync(anon, ev.Slug, Body(ev.TicketId, [Person(email), Member()]))).StatusCode); // 4 emails
        // Third for the same buyer: per-email limit.
        Assert.Equal(HttpStatusCode.TooManyRequests, (await RegisterAsync(anon, ev.Slug, Body(ev.TicketId, [Person(email), Member()]))).StatusCode);
        // Another buyer from the same IP: 4 + 2 > 5 emails, refused.
        var other = await RegisterAsync(anon, ev.Slug, Body(ev.TicketId, [Person(), Member()]));
        Assert.Equal(HttpStatusCode.TooManyRequests, other.StatusCode);
        Assert.Equal("rate_limited", (await ApiClient.JsonAsync(other)).Code());
    }
}
