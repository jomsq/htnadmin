using System.Net;
using System.Security.Cryptography.X509Certificates;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Email;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;

namespace Htn.Api.Hosting;

/// <summary>Startup wiring that depends on deployment configuration, with fail-fast checks outside Development.</summary>
public static class HostingSetup
{
    public const string DataProtectionCertificatePath = "DataProtection:CertificatePath";
    public const string DataProtectionCertificatePassword = "DataProtection:CertificatePassword";
    public const string DataProtectionAllowUnprotectedKeys = "DataProtection:AllowUnprotectedKeys";
    public const string EmailAllowLoggingSender = "Email:AllowLoggingSender";

    /// <summary>
    /// Keys are stored in Postgres. When <c>DataProtection:CertificatePath</c> points to a PFX, keys are encrypted at rest
    /// with it. Production refuses to start without one unless <c>DataProtection:AllowUnprotectedKeys=true</c>.
    /// </summary>
    public static IDataProtectionBuilder AddHtnDataProtection(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        var dp = services.AddDataProtection().SetApplicationName("htn-admin").PersistKeysToDbContext<HtnDbContext>();

        var path = config[DataProtectionCertificatePath];
        if (!string.IsNullOrWhiteSpace(path))
        {
            var cert = LoadCertificate(path, config[DataProtectionCertificatePassword]);
            dp.ProtectKeysWithCertificate(cert).UnprotectKeysWithAnyCertificate(cert);
        }
        else if (env.IsProduction() && !config.GetValue<bool>(DataProtectionAllowUnprotectedKeys))
        {
            throw new InvalidOperationException(
                "Data Protection keys would be stored unencrypted. Set DataProtection__CertificatePath to a PFX with a private key " +
                "(and DataProtection__CertificatePassword if it has one), or set DataProtection__AllowUnprotectedKeys=true to accept the risk.");
        }
        return dp;
    }

    public static X509Certificate2 LoadCertificate(string path, string? password)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"DataProtection:CertificatePath '{path}' does not exist.");
        X509Certificate2 cert;
        try
        {
            cert = X509CertificateLoader.LoadPkcs12FromFile(path, string.IsNullOrEmpty(password) ? null : password);
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            throw new InvalidOperationException("DataProtection:CertificatePath could not be loaded as a PFX (wrong password or format).", ex);
        }
        if (!cert.HasPrivateKey)
        {
            cert.Dispose();
            throw new InvalidOperationException("The Data Protection certificate has no private key, so keys could not be decrypted.");
        }
        return cert;
    }

    /// <summary>Outside Development, a missing MAILTRAP_API_TOKEN would silently drop invites and resets; refuse to start.</summary>
    public static void EnsureEmailConfigured(IConfiguration config, IHostEnvironment env)
    {
        if (env.IsDevelopment() || MailtrapOptions.FromConfiguration(config).IsConfigured) return;
        if (config.GetValue<bool>(EmailAllowLoggingSender)) return;
        throw new InvalidOperationException(
            $"MAILTRAP_API_TOKEN is not set in the {env.EnvironmentName} environment, so no email would be sent. " +
            "Set MAILTRAP_API_TOKEN, or set Email__AllowLoggingSender=true to log (masked) instead of sending.");
    }

    /// <summary>
    /// Trust X-Forwarded-For/Proto from the reverse proxy. <c>ForwardedHeaders:KnownProxies</c> (IPs) and
    /// <c>ForwardedHeaders:KnownNetworks</c> (CIDRs) restrict who is trusted (comma-separated or arrays);
    /// <c>ForwardedHeaders:ForwardLimit</c> (default 1) is how many proxy hops are read. With neither list set, any
    /// direct peer is trusted: only safe when the API is reachable solely through nginx, which overwrites X-Forwarded-For.
    /// </summary>
    public static void ConfigureForwardedHeaders(ForwardedHeadersOptions o, IConfiguration config)
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.ForwardLimit = config.GetValue<int?>("ForwardedHeaders:ForwardLimit") is { } limit and > 0 ? limit : 1;
        o.KnownIPNetworks.Clear();
        o.KnownProxies.Clear();
        foreach (var ip in ReadList(config, "ForwardedHeaders:KnownProxies"))
            o.KnownProxies.Add(IPAddress.TryParse(ip, out var addr) ? addr
                : throw new InvalidOperationException($"ForwardedHeaders:KnownProxies has an invalid IP address '{ip}'."));
        foreach (var cidr in ReadList(config, "ForwardedHeaders:KnownNetworks"))
            o.KnownIPNetworks.Add(System.Net.IPNetwork.TryParse(cidr, out var net) ? net
                : throw new InvalidOperationException($"ForwardedHeaders:KnownNetworks has an invalid CIDR '{cidr}'."));
    }

    internal static IEnumerable<string> ReadList(IConfiguration config, string key)
    {
        var section = config.GetSection(key);
        var values = string.IsNullOrWhiteSpace(section.Value)
            ? section.GetChildren().Select(c => c.Value)
            : [section.Value];
        return values
            .SelectMany(v => (v ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToArray();
    }
}
