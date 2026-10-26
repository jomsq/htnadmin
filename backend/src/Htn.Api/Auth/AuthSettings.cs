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

    /// <summary>Per client IP, per window of <see cref="PublicWindowSeconds"/>, for the public registration endpoints.</summary>
    public int PublicReadPermitLimit { get; set; } = 120;
    /// <summary>Voucher attempts per IP: voucher checks and registrations that carry a code share this bucket.</summary>
    public int PublicVoucherPermitLimit { get; set; } = 10;
    public int PublicRegisterPermitLimit { get; set; } = 10;
    public int PublicProofPermitLimit { get; set; } = 20;
    public int PublicWindowSeconds { get; set; } = 60;

    /// <summary>Check-in scans and manual check-ins per signed-in user, per window (a busy desk does about one every 3 s).</summary>
    public int CheckInPermitLimit { get; set; } = 120;

    /// <summary>Check-in desk searches per signed-in user, per window.</summary>
    public int CheckInSearchPermitLimit { get; set; } = 120;

    public int CheckInWindowSeconds { get; set; } = 60;

    /// <summary>
    /// Emails a client IP can trigger through public checkouts (one per attendee) per window: limits using the form to
    /// mail-bomb other people's addresses.
    /// </summary>
    public int PublicEmailsPerIpPermitLimit { get; set; } = 30;
    public int PublicEmailsPerIpWindowSeconds { get; set; } = 60 * 60;

    /// <summary>Public registrations per normalized buyer email.</summary>
    public int RegisterPerEmailPermitLimit { get; set; } = 5;
    public int RegisterPerEmailWindowSeconds { get; set; } = 60 * 60;
}
