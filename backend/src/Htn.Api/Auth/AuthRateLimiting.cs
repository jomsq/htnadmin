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
        services.AddSingleton<PublicAttemptLimiter>();
        services.AddSingleton<MentionEmailLimiter>();
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = (ctx, _) => new ValueTask(TooManyAsync(ctx.HttpContext));
            foreach (var policy in Policies)
                o.AddPolicy(policy, http => PerIp(policy, http, s => s.AuthPermitLimit, s => s.AuthWindowSeconds));
            foreach (var policy in PublicRateLimiting.PolicyNames)
                o.AddPolicy(policy, http => PerIp(policy, http, s => PublicRateLimiting.PermitLimit(policy, s), s => s.PublicWindowSeconds));
            o.AddPolicy(CheckInRateLimiting.Scan, http => CheckInRateLimiting.PerUser(CheckInRateLimiting.Scan, http, s => s.CheckInPermitLimit));
            o.AddPolicy(CheckInRateLimiting.Search, http => CheckInRateLimiting.PerUser(CheckInRateLimiting.Search, http, s => s.CheckInSearchPermitLimit));
            o.AddPolicy(CheckInRateLimiting.Read, http => CheckInRateLimiting.PerUser(CheckInRateLimiting.Read, http, s => s.CheckInReadPermitLimit));
            o.AddPolicy(TaskRateLimiting.Write, http => TaskRateLimiting.PerUser(TaskRateLimiting.Write, http));
        });
        return services;
    }

    /// <summary>
    /// The rate-limit key for the client address. IPv4-mapped IPv6 is normalized to IPv4, and native IPv6 is reduced to
    /// its /64 prefix (one subscriber usually gets a whole /64, so per-address buckets would be trivial to rotate).
    /// </summary>
    internal static string ClientIp(HttpContext http) => ClientKey(http.Connection.RemoteIpAddress);

    public static string ClientKey(System.Net.IPAddress? ip)
    {
        if (ip is null) return "unknown";
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6) return ip.ToString();
        var bytes = ip.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new System.Net.IPAddress(bytes) + "/64";
    }

    private static RateLimitPartition<string> PerIp(
        string policy, HttpContext http, Func<RateLimitSettings, int> permits, Func<RateLimitSettings, int> windowSeconds)
    {
        var s = http.RequestServices.GetRequiredService<IOptions<RateLimitSettings>>().Value;
        var key = ClientIp(http);
        return RateLimitPartition.GetFixedWindowLimiter($"{policy}|{key}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, permits(s)),
            Window = TimeSpan.FromSeconds(Math.Max(1, windowSeconds(s))),
            QueueLimit = 0,
        });
    }

    public static Task TooManyAsync(HttpContext http) =>
        Problems.WriteAsync(http, StatusCodes.Status429TooManyRequests, "Too many attempts", "Wait a few minutes and try again.", "rate_limited");

    public static IResult TooMany() =>
        Problems.Result(StatusCodes.Status429TooManyRequests, "Too many attempts", "Wait a few minutes and try again.", "rate_limited");
}

/// <summary>
/// Per-email fixed windows for login, forgot-password and public registration (by the buyer's email). Applied before any account lookup, so the limit (and the
/// 429) is the same for existing and unknown emails. Partition keys are SHA-256 hashes of the normalized email, so
/// raw addresses aren't kept in memory.
/// </summary>
public sealed class EmailRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _login;
    private readonly PartitionedRateLimiter<string> _forgot;
    private readonly PartitionedRateLimiter<string> _register;

    public EmailRateLimiter(IOptions<RateLimitSettings> options)
    {
        var s = options.Value;
        _login = Create(s.LoginPerEmailPermitLimit, s.LoginPerEmailWindowSeconds);
        _forgot = Create(s.ForgotPerEmailPermitLimit, s.ForgotPerEmailWindowSeconds);
        _register = Create(s.RegisterPerEmailPermitLimit, s.RegisterPerEmailWindowSeconds);
    }

    public bool TryLogin(string email) => TryAcquire(_login, email);

    public bool TryForgot(string email) => TryAcquire(_forgot, email);

    public bool TryRegister(string email) => TryAcquire(_register, email);

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
        _register.Dispose();
    }
}

/// <summary>
/// Per-IP fixed windows for the anonymous registration endpoints, each with its own bucket and limit. Partitioned by
/// client IP only, like the auth policies, so slugs or tokens in the path don't open new buckets.
/// </summary>
public static class PublicRateLimiting
{
    public const string Read = "public-read";
    public const string Voucher = "public-voucher";
    public const string Register = "public-register";
    public const string Proof = "public-proof";

    /// <summary>Endpoint policies. Voucher attempts are counted by <see cref="PublicAttemptLimiter"/> instead (shared bucket).</summary>
    public static readonly IReadOnlyList<string> PolicyNames = [Read, Register, Proof];

    public static int PermitLimit(string policy, RateLimitSettings s) => policy switch
    {
        Read => s.PublicReadPermitLimit,
        Voucher => s.PublicVoucherPermitLimit,
        Register => s.PublicRegisterPermitLimit,
        _ => s.PublicProofPermitLimit,
    };
}

/// <summary>
/// Per-IP buckets the public endpoints consume by hand: voucher attempts (voucher-check and registrations carrying a
/// code share one bucket, so the registration endpoint can't be used to brute-force codes past the check's limit), and
/// emails triggered by public checkouts (one permit per attendee).
/// </summary>
public sealed class PublicAttemptLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _vouchers;
    private readonly PartitionedRateLimiter<string> _emails;
    private readonly int _emailPermits;

    public PublicAttemptLimiter(IOptions<RateLimitSettings> options)
    {
        var s = options.Value;
        _vouchers = Create(s.PublicVoucherPermitLimit, s.PublicWindowSeconds);
        _emailPermits = Math.Max(1, s.PublicEmailsPerIpPermitLimit);
        _emails = Create(_emailPermits, s.PublicEmailsPerIpWindowSeconds);
    }

    public bool TryVoucher(HttpContext http) => TryAcquire(_vouchers, http, 1);

    public bool TryEmails(HttpContext http, int count) => count <= _emailPermits && TryAcquire(_emails, http, count);

    private static bool TryAcquire(PartitionedRateLimiter<string> limiter, HttpContext http, int permits)
    {
        using var lease = limiter.AttemptAcquire(AuthRateLimiting.ClientIp(http), Math.Max(1, permits));
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
        _vouchers.Dispose();
        _emails.Dispose();
    }
}

/// <summary>
/// Per signed-in user fixed windows for the check-in desk (scan + manual share one bucket; search, and stats + recent,
/// have their own), so one station can't hammer token checks or page through the attendee list. Anonymous requests (rejected with 401 later)
/// fall back to a per-IP bucket.
/// </summary>
public static class CheckInRateLimiting
{
    public const string Scan = "checkin-scan";
    public const string Search = "checkin-search";

    /// <summary>Stats and recent scans (polled by the desk), one bucket for both.</summary>
    public const string Read = "checkin-read";

    internal static RateLimitPartition<string> PerUser(string policy, HttpContext http, Func<RateLimitSettings, int> permits)
    {
        var s = http.RequestServices.GetRequiredService<IOptions<RateLimitSettings>>().Value;
        var userId = http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var key = userId is { Length: > 0 } ? "user:" + userId : "ip:" + AuthRateLimiting.ClientIp(http);
        return RateLimitPartition.GetFixedWindowLimiter($"{policy}|{key}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, permits(s)),
            Window = TimeSpan.FromSeconds(Math.Max(1, s.CheckInWindowSeconds)),
            QueueLimit = 0,
        });
    }
}

/// <summary>
/// One fixed window per signed-in user for every mutating task and committee route (<c>tasks-write</c>). A board or a
/// task dialog is a handful of writes per minute even when someone is dragging cards around; this keeps one account
/// from using the write paths — each of which fans out into activity rows, outbox messages and audit entries — as an
/// amplifier. Anonymous requests (rejected with 401 later) fall back to a per-IP bucket, exactly like the check-in
/// desk limiter. Limits come from <c>RateLimiting:TasksWritePermitLimit</c> / <c>:TasksWriteWindowSeconds</c>.
/// </summary>
public static class TaskRateLimiting
{
    public const string Write = "tasks-write";

    internal static RateLimitPartition<string> PerUser(string policy, HttpContext http)
    {
        var s = http.RequestServices.GetRequiredService<IOptions<RateLimitSettings>>().Value;
        var userId = http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var key = userId is { Length: > 0 } ? "user:" + userId : "ip:" + AuthRateLimiting.ClientIp(http);
        return RateLimitPartition.GetFixedWindowLimiter($"{policy}|{key}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, s.TasksWritePermitLimit),
            Window = TimeSpan.FromSeconds(Math.Max(1, s.TasksWriteWindowSeconds)),
            QueueLimit = 0,
        });
    }
}

/// <summary>
/// The @mention email budget: how many recipients one author can trigger per window, counted the way
/// <see cref="PublicAttemptLimiter.TryEmails"/> counts public checkout emails. A comment may name ten people, so
/// without this one goon could mail the whole organization in a few minutes. Overflow recipients are dropped
/// silently and counted in the audit entry; the comment itself is still posted.
/// </summary>
public sealed class MentionEmailLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _emails;

    public MentionEmailLimiter(IOptions<RateLimitSettings> options)
    {
        var s = options.Value;
        _emails = PartitionedRateLimiter.Create<string, string>(key =>
            RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, s.MentionEmailsPerAuthorPermitLimit),
                Window = TimeSpan.FromSeconds(Math.Max(1, s.MentionEmailsPerAuthorWindowSeconds)),
                QueueLimit = 0,
            }));
    }

    /// <summary>One permit = one mention email. False means this recipient is dropped.</summary>
    public bool TryRecipient(Guid authorUserId)
    {
        using var lease = _emails.AttemptAcquire(authorUserId.ToString());
        return lease.IsAcquired;
    }

    public void Dispose() => _emails.Dispose();
}
