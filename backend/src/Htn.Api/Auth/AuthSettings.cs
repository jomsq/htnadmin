namespace Htn.Api.Auth;

public sealed class AuthSettings
{
    public const string Section = "Auth";

    /// <summary>Board accounts must enable TOTP before they can use anything but the 2FA setup endpoints.</summary>
    public bool RequireBoard2fa { get; set; } = true;

    /// <summary>How often the cookie's security stamp is re-checked (deactivation / password change ends sessions).</summary>
    public TimeSpan SecurityStampValidationInterval { get; set; } = TimeSpan.FromMinutes(1);

    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromHours(8);
}

public sealed class RateLimitSettings
{
    public const string Section = "RateLimiting";

    /// <summary>Requests per window, per client IP, for each auth policy (login, 2FA, forgot, reset, invite).</summary>
    public int AuthPermitLimit { get; set; } = 10;
    public int AuthWindowSeconds { get; set; } = 60;

    /// <summary>Login attempts per normalized email, whether or not an account exists.</summary>
    public int LoginPerEmailPermitLimit { get; set; } = 10;
    public int LoginPerEmailWindowSeconds { get; set; } = 15 * 60;

    /// <summary>Password-reset requests per normalized email, whether or not an account exists.</summary>
    public int ForgotPerEmailPermitLimit { get; set; } = 5;
    public int ForgotPerEmailWindowSeconds { get; set; } = 10 * 60;
}
