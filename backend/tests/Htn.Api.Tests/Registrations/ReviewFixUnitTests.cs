using System.Net;
using Htn.Api.Auth;
using Htn.Api.Hosting;
using Htn.Api.Tests.Hosting;
using Htn.Infrastructure.Tickets;
using Microsoft.Extensions.Configuration;

namespace Htn.Api.Tests.Registrations;

public sealed class ReviewFixUnitTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    [Theory]
    [InlineData(TicketSigning.DevelopmentKey)]
    [InlineData("htn-dev-only-ticket-signing-key-not-for-production")]
    [InlineData("  htn-dev-only-ticket-signing-key-not-for-production  ")]
    public void Published_signing_keys_are_refused_outside_development(string key)
    {
        foreach (var env in new[] { "Production", "Staging", "Testing" })
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                TicketSigning.EnsureConfigured(Config((TicketSigning.EnvironmentVariable, key)), new FakeEnvironment(env)));
            Assert.Contains("published development key", ex.Message, StringComparison.Ordinal);
        }
        TicketSigning.EnsureConfigured(Config((TicketSigning.EnvironmentVariable, key.Trim())), new FakeEnvironment("Development"));
        // An empty value (the dev compose default now) means "not set": the dev key in Development, refused elsewhere.
        TicketSigning.EnsureConfigured(Config((TicketSigning.EnvironmentVariable, "")), new FakeEnvironment("Development"));
        Assert.Throws<InvalidOperationException>(() =>
            TicketSigning.EnsureConfigured(Config((TicketSigning.EnvironmentVariable, "")), new FakeEnvironment("Production")));
    }

    [Theory]
    [InlineData("https://admin.hackthenorth.ph", true)]
    [InlineData("https://admin.hackthenorth.ph/", true)]
    [InlineData("http://admin.hackthenorth.ph", false)]
    [InlineData("admin.hackthenorth.ph", false)]
    [InlineData("/relative", false)]
    [InlineData("https://admin.hackthenorth.ph/?x=1", false)]
    [InlineData(null, false)]
    public void Public_base_url_must_be_absolute_https_outside_development(string? url, bool ok)
    {
        var config = Config(("App:PublicBaseUrl", url));
        if (ok) HostingSetup.EnsurePublicBaseUrl(config, new FakeEnvironment("Production"));
        else Assert.Throws<InvalidOperationException>(() => HostingSetup.EnsurePublicBaseUrl(config, new FakeEnvironment("Production")));
        HostingSetup.EnsurePublicBaseUrl(config, new FakeEnvironment("Development")); // anything goes in Development
    }

    [Fact]
    public void Ipv6_clients_share_a_bucket_per_64_and_mapped_ipv4_is_normalized()
    {
        static string Key(string ip) => AuthRateLimiting.ClientKey(IPAddress.Parse(ip));
        Assert.Equal(Key("2001:db8:1:2:aaaa:bbbb:cccc:1"), Key("2001:db8:1:2::ffff"));
        Assert.Equal("2001:db8:1:2::/64", Key("2001:db8:1:2:aaaa::1"));
        Assert.NotEqual(Key("2001:db8:1:2::1"), Key("2001:db8:1:3::1"));
        Assert.Equal("203.0.113.7", Key("::ffff:203.0.113.7"));
        Assert.Equal("203.0.113.7", Key("203.0.113.7"));
        Assert.Equal("unknown", AuthRateLimiting.ClientKey(null));
    }
}
