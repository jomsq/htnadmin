using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Htn.Api.Common;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Htn.Api.Auth;

/// <summary>
/// One fixed-window limiter per auth policy, partitioned by client IP only (never by path, so casing, trailing
/// slashes or tokens in the URL can't open new buckets). Login and forgot-password also have a per-email limit,
/// see <see cref="EmailRateLimiter"/>.
/// </summary>
public static class AuthRateLimiting
{
    public const string Login = "auth-login";
    public const string TwoFactor = "auth-2fa";
    public const string Forgot = "auth-forgot";
    public const string Reset = "auth-reset";
    public const string Invite = "auth-invite";

    public static readonly IReadOnlyList<string> Policies = [Login, TwoFactor, Forgot, Reset, Invite];

    public static IServiceCollection AddHtnRateLimiting(this IServiceCollection services)
    {
        services.AddSingleton<EmailRateLimiter>();
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = (ctx, _) => new ValueTask(TooManyAsync(ctx.HttpContext));
            foreach (var policy in Policies)
                o.AddPolicy(policy, http => PerIp(policy, http));
        });
        return services;
    }

    private static RateLimitPartition<string> PerIp(string policy, HttpContext http)
    {
        var s = http.RequestServices.GetRequiredService<IOptions<RateLimitSettings>>().Value;
        var ip = http.Connection.RemoteIpAddress;
        // Normalize IPv4-mapped IPv6 so one client can't get two buckets.
        var key = ip is null ? "unknown" : (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString();
        return RateLimitPartition.GetFixedWindowLimiter($"{policy}|{key}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = s.AuthPermitLimit,
            Window = TimeSpan.FromSeconds(s.AuthWindowSeconds),
            QueueLimit = 0,
        });
    }

    public static Task TooManyAsync(HttpContext http) =>
        Problems.WriteAsync(http, StatusCodes.Status429TooManyRequests, "Too many attempts", "Wait a few minutes and try again.", "rate_limited");

    public static IResult TooMany() =>
        Problems.Result(StatusCodes.Status429TooManyRequests, "Too many attempts", "Wait a few minutes and try again.", "rate_limited");
}

/// <summary>
/// Per-email fixed windows for login and forgot-password. Applied before any account lookup, so the limit (and the
/// 429) is the same for existing and unknown emails. Partition keys are SHA-256 hashes of the normalized email, so
/// raw addresses aren't kept in memory.
/// </summary>
public sealed class EmailRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _login;
    private readonly PartitionedRateLimiter<string> _forgot;

    public EmailRateLimiter(IOptions<RateLimitSettings> options)
    {
        var s = options.Value;
        _login = Create(s.LoginPerEmailPermitLimit, s.LoginPerEmailWindowSeconds);
        _forgot = Create(s.ForgotPerEmailPermitLimit, s.ForgotPerEmailWindowSeconds);
    }

    public bool TryLogin(string email) => TryAcquire(_login, email);

    public bool TryForgot(string email) => TryAcquire(_forgot, email);

    public static string Key(string email) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToUpperInvariant())));

    private static bool TryAcquire(PartitionedRateLimiter<string> limiter, string email)
    {
        using var lease = limiter.AttemptAcquire(Key(email));
        return lease.IsAcquired;
    }

    private static PartitionedRateLimiter<string> Create(int permits, int windowSeconds) =>
        PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, permits),
            Window = TimeSpan.FromSeconds(Math.Max(1, windowSeconds)),
            QueueLimit = 0,
        }));

    public void Dispose()
    {
        _login.Dispose();
        _forgot.Dispose();
    }
}
