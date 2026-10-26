using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Htn.Api.Hosting;
using Htn.Api.Seeding;
using Htn.Api.Tests.Infrastructure;
using Htn.Infrastructure.Email;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Htn.Api.Tests.Hosting;

internal sealed class FakeEnvironment(string name) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = name;
    public string ApplicationName { get; set; } = "Htn.Api";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

internal static class TestCertificates
{
    /// <summary>Writes a self-signed RSA PFX (with private key) to a temp file and returns its path.</summary>
    public static string WritePfx(string? password)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=htn-dataprotection-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var path = Path.Combine(Path.GetTempPath(), $"htn-dp-{Guid.NewGuid():N}.pfx");
        File.WriteAllBytes(path, cert.Export(X509ContentType.Pfx, password));
        return path;
    }
}

public sealed class HostingSetupTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    [Theory]
    [InlineData("Development", "true", true)]
    [InlineData("Development", null, false)]
    [InlineData("Development", "false", false)]
    [InlineData("Production", "true", false)]
    [InlineData("Staging", "true", false)]
    public void Dev_seed_needs_development_and_explicit_flag(string env, string? flag, bool expected) =>
        Assert.Equal(expected, DevSeeder.ShouldRun(new FakeEnvironment(env), Config((DevSeeder.ConfigKey, flag))));

    [Fact]
    public void Missing_mailtrap_token_fails_outside_development()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            HostingSetup.EnsureEmailConfigured(Config(), new FakeEnvironment("Production")));
        Assert.Contains("MAILTRAP_API_TOKEN", ex.Message, StringComparison.Ordinal);

        HostingSetup.EnsureEmailConfigured(Config(), new FakeEnvironment("Development"));
        HostingSetup.EnsureEmailConfigured(Config(("MAILTRAP_API_TOKEN", "x")), new FakeEnvironment("Production"));
        HostingSetup.EnsureEmailConfigured(Config((HostingSetup.EmailAllowLoggingSender, "true")), new FakeEnvironment("Staging"));
    }

    [Fact]
    public void Production_without_data_protection_certificate_fails_unless_explicitly_allowed()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddHtnDataProtection(Config(), new FakeEnvironment("Production")));
        Assert.Contains("DataProtection__CertificatePath", ex.Message, StringComparison.Ordinal);

        new ServiceCollection().AddHtnDataProtection(Config((HostingSetup.DataProtectionAllowUnprotectedKeys, "true")), new FakeEnvironment("Production"));
        new ServiceCollection().AddHtnDataProtection(Config(), new FakeEnvironment("Staging"));
    }

    [Fact]
    public void Data_protection_certificate_errors_are_clear()
    {
        var missing = Assert.Throws<InvalidOperationException>(() => HostingSetup.LoadCertificate("/nope/missing.pfx", null));
        Assert.Contains("does not exist", missing.Message, StringComparison.Ordinal);

        var path = TestCertificates.WritePfx("right");
        try
        {
            Assert.Throws<InvalidOperationException>(() => HostingSetup.LoadCertificate(path, "wrong"));
            using var cert = HostingSetup.LoadCertificate(path, "right");
            Assert.True(cert.HasPrivateKey);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Forwarded_headers_read_proxies_networks_and_limit()
    {
        var o = new ForwardedHeadersOptions();
        HostingSetup.ConfigureForwardedHeaders(o, Config(
            ("ForwardedHeaders:KnownProxies", "10.0.0.5, 10.0.0.6"),
            ("ForwardedHeaders:KnownNetworks:0", "172.16.0.0/12"),
            ("ForwardedHeaders:KnownNetworks:1", "192.168.0.0/16"),
            ("ForwardedHeaders:ForwardLimit", "2")));
        Assert.Equal(2, o.KnownProxies.Count);
        Assert.Equal(2, o.KnownIPNetworks.Count);
        Assert.Equal(2, o.ForwardLimit);

        var defaults = new ForwardedHeadersOptions();
        HostingSetup.ConfigureForwardedHeaders(defaults, Config(("ForwardedHeaders:KnownProxies", "")));
        Assert.Empty(defaults.KnownProxies);
        Assert.Empty(defaults.KnownIPNetworks);
        Assert.Equal(1, defaults.ForwardLimit);

        Assert.Throws<InvalidOperationException>(() =>
            HostingSetup.ConfigureForwardedHeaders(new ForwardedHeadersOptions(), Config(("ForwardedHeaders:KnownNetworks", "not-a-cidr"))));
    }

    [Fact]
    public async Task Logging_sender_masks_recipient_and_omits_body_outside_development()
    {
        var logger = new CapturingLogger();
        var sender = new LoggingEmailSender(logger, new FakeEnvironment("Staging"));
        await sender.SendAsync(new EmailMessage("mika@hackthenorth.ph", "Mika", "Reset your password", "<p>x</p>",
            "https://admin.test/reset/secret-token", "password_reset"), CancellationToken.None);

        var line = Assert.Single(logger.Lines);
        Assert.Contains("m•••@hackthenorth.ph", line, StringComparison.Ordinal);
        Assert.DoesNotContain("mika@", line, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", line, StringComparison.Ordinal);
    }

    private sealed class CapturingLogger : ILogger<LoggingEmailSender>
    {
        public List<string> Lines { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception));
    }
}

/// <summary>Host configured with a Data Protection certificate: keys in the database must be encrypted with it.</summary>
public sealed class CertificateDataProtectionApiFactory : HtnApiFactory
{
    public string CertificatePath { get; } = TestCertificates.WritePfx("test-pfx-password");

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        [HostingSetup.DataProtectionCertificatePath] = CertificatePath,
        [HostingSetup.DataProtectionCertificatePassword] = "test-pfx-password",
    };

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        File.Delete(CertificatePath);
        GC.SuppressFinalize(this);
    }
}

public sealed class DataProtectionAtRestTests(CertificateDataProtectionApiFactory factory) : IClassFixture<CertificateDataProtectionApiFactory>
{
    [Fact]
    public async Task Keys_are_stored_encrypted_with_the_certificate_and_still_work()
    {
        var user = await factory.CreateUserAsync(Htn.Domain.Organization.PersonLevel.Goon);
        var client = factory.NewClient();
        await client.LoginOkAsync(user.Email, HtnApiFactory.Password); // antiforgery + auth cookie use the key ring
        Assert.Equal(System.Net.HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

        var keys = await factory.DbAsync(db => db.DataProtectionKeys.AsNoTracking().Select(k => k.Xml).ToListAsync());
        Assert.NotEmpty(keys);
        Assert.All(keys, xml =>
        {
            Assert.Contains("EncryptedXmlDecryptor", xml, StringComparison.Ordinal);
            Assert.DoesNotContain("<masterKey", xml, StringComparison.Ordinal);
        });
    }
}
