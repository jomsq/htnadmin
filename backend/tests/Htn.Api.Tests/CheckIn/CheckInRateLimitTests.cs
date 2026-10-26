using System.Net;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;
using static Htn.Api.Tests.CheckIn.CheckInTestData;
using static Htn.Api.Tests.Registrations.RegistrationTestData;

namespace Htn.Api.Tests.CheckIn;

public sealed class CheckInRateLimitApiFactory : HtnApiFactory
{
    protected override IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>
    {
        ["RateLimiting:CheckInPermitLimit"] = "3",
        ["RateLimiting:CheckInSearchPermitLimit"] = "2",
        ["RateLimiting:CheckInWindowSeconds"] = "600",
    };
}

public sealed class CheckInRateLimitTests(CheckInRateLimitApiFactory factory) : IClassFixture<CheckInRateLimitApiFactory>
{
    [Fact]
    public async Task Scans_are_limited_per_user_and_share_a_bucket_with_manual()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board);
        var staff = await factory.SignedInAsync(PersonLevel.Goon);

        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsync(ScanUrl(ev.Id), Scan("junk 1"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsync(ScanUrl(ev.Id), Scan("junk 2"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsync($"/api/events/{ev.Id}/check-in/manual", new { attendeeId = Guid.NewGuid(), station = "Desk" })).StatusCode);
        var limited = await staff.PostAsync(ScanUrl(ev.Id), Scan("junk 3"));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("rate_limited", (await ApiClient.JsonAsync(limited)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await staff.PostAsync($"/api/events/{ev.Id}/check-in/manual", new { attendeeId = Guid.NewGuid(), station = "Desk" })).StatusCode);

        // Another signed-in user on the same IP has their own bucket; reads aren't limited.
        var other = await factory.SignedInAsync(PersonLevel.Apprentice);
        Assert.Equal(HttpStatusCode.OK, (await other.PostAsync(ScanUrl(ev.Id), Scan("junk 4"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync($"/api/events/{ev.Id}/check-in/stats")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync($"/api/events/{ev.Id}/check-in/recent")).StatusCode);
    }

    [Fact]
    public async Task Search_has_its_own_per_user_limit()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var ev = await PublishedAsync(board);
        var staff = await factory.SignedInAsync(PersonLevel.Goon);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync($"/api/events/{ev.Id}/check-in/search?q=maria")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync($"/api/events/{ev.Id}/check-in/search?q=juan")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await staff.GetAsync($"/api/events/{ev.Id}/check-in/search?q=ana")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsync(ScanUrl(ev.Id), Scan("junk"))).StatusCode);
    }
}
