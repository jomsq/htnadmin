using System.Text;
using System.Text.Encodings.Web;
using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Email;
using Htn.Infrastructure.Identity;
using Htn.Infrastructure.Outbox;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Auth;

public static class AuthEndpoints
{
    public const string TotpIssuer = "HackTheNorth.PH";
    private const string InvalidCredentials = "Invalid email or password.";
    private static string? _dummyHash;

    public static RouteGroupBuilder MapAuth(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/auth").WithTags("Auth").AllowWithoutTwoFactor();

        g.MapGet("/antiforgery", GetAntiforgery).AllowAnonymous();

        g.MapPost("/login", Login).AllowAnonymous().RequireRateLimiting(AuthRateLimiting.Login).Validate<LoginRequest>();
        g.MapPost("/login/2fa", LoginTwoFactor).AllowAnonymous().RequireRateLimiting(AuthRateLimiting.TwoFactor).Validate<LoginTwoFactorRequest>();
        g.MapPost("/logout", Logout).RequireAuthorization();
        g.MapGet("/me", Me).RequireAuthorization();

        g.MapGet("/invite/{token}", GetInvite).AllowAnonymous().RequireRateLimiting(AuthRateLimiting.Invite);
        g.MapPost("/invite/accept", AcceptInvite).AllowAnonymous().RequireRateLimiting(AuthRateLimiting.Invite).Validate<AcceptInviteRequest>();
        g.MapPost("/password/forgot", ForgotPassword).AllowAnonymous().RequireRateLimiting(AuthRateLimiting.Forgot).Validate<ForgotPasswordRequest>();
        g.MapPost("/password/reset", ResetPassword).AllowAnonymous().RequireRateLimiting(AuthRateLimiting.Reset).Validate<ResetPasswordRequest>();

        g.MapPost("/2fa/setup", SetupTwoFactor).RequireAuthorization();
        g.MapPost("/2fa/enable", EnableTwoFactor).RequireAuthorization().RequireRateLimiting(AuthRateLimiting.TwoFactor).Validate<EnableTwoFactorRequest>();

        return g;
    }

    private static IResult Unauthorized() => Problems.Result(StatusCodes.Status401Unauthorized, InvalidCredentials);

    private static Ok<AntiforgeryTokenDto> GetAntiforgery(HttpContext http, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(http);
        return TypedResults.Ok(new AntiforgeryTokenDto(tokens.RequestToken!));
    }

    private static async Task<IResult> Login(
        LoginRequest req, UserManager<AppUser> users, SignInManager<AppUser> signIn, IPasswordHasher<AppUser> hasher,
        MeService me, EmailRateLimiter emailLimits, TimeProvider clock, CancellationToken ct)
    {
        // Counted before the lookup so unknown and existing emails are limited identically.
        if (!emailLimits.TryLogin(req.Email)) return AuthRateLimiting.TooMany();

        var user = await users.FindByEmailAsync(req.Email.Trim());
        if (user is null)
        {
            // Same work as a real check so response time doesn't reveal whether the email exists.
            _dummyHash ??= hasher.HashPassword(new AppUser(), "not-a-real-password-0000");
            hasher.VerifyHashedPassword(new AppUser(), _dummyHash, req.Password);
            return Unauthorized();
        }

        var result = await signIn.PasswordSignInAsync(user, req.Password, isPersistent: req.RememberMe == true, lockoutOnFailure: true);
        if (result.RequiresTwoFactor)
            return TypedResults.Accepted((string?)null, new LoginChallengeDto(true));
        if (!result.Succeeded)
            return Unauthorized();

        return await CompleteSignIn(user, users, signIn, me, clock, ct);
    }

    private static async Task<IResult> LoginTwoFactor(
        LoginTwoFactorRequest req, UserManager<AppUser> users, SignInManager<AppUser> signIn, MeService me, TimeProvider clock, CancellationToken ct)
    {
        var user = await signIn.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
            return Problems.Result(StatusCodes.Status401Unauthorized, "Your sign-in session expired. Sign in again.");

        var result = await signIn.TwoFactorAuthenticatorSignInAsync(NormalizeCode(req.Code), isPersistent: req.RememberMe == true, rememberClient: false);
        if (!result.Succeeded)
            return Problems.Result(StatusCodes.Status401Unauthorized, "The code is not valid.");

        return await CompleteSignIn(user, users, signIn, me, clock, ct);
    }

    private static async Task<IResult> CompleteSignIn(
        AppUser user, UserManager<AppUser> users, SignInManager<AppUser> signIn, MeService me, TimeProvider clock, CancellationToken ct)
    {
        user.LastSignInAt = clock.GetUtcNow();
        await users.UpdateAsync(user);
        var principal = await signIn.CreateUserPrincipalAsync(user);
        return TypedResults.Ok(await me.BuildAsync(user, principal, ct));
    }

    private static async Task<NoContent> Logout(SignInManager<AppUser> signIn)
    {
        await signIn.SignOutAsync();
        return TypedResults.NoContent();
    }

    private static async Task<IResult> Me(HttpContext http, UserManager<AppUser> users, MeService me, CancellationToken ct)
    {
        var user = await users.GetUserAsync(http.User);
        if (user is null || !user.IsActive) return TypedResults.Unauthorized();
        return TypedResults.Ok(await me.BuildAsync(user, http.User, ct));
    }

    private static async Task<IResult> GetInvite(string token, HtnDbContext db, IAccountTokenService tokens, CancellationToken ct)
    {
        var t = await tokens.FindUsableAsync(token, AccountTokenPurpose.Invite, ct);
        if (t is null) return TypedResults.NotFound();
        var info = await (
            from u in db.Users.AsNoTracking()
            join p in db.People on u.PersonId equals (Guid?)p.Id
            where u.Id == t.UserId && u.IsActive && u.PasswordHash == null && p.IsActive
            select new { u.Email, p.FirstName, p.Level }).SingleOrDefaultAsync(ct);
        if (info is null) return TypedResults.NotFound();

        string? inviter = null;
        if (t.IssuedById is { } issuerId)
            inviter = await (
                from u in db.Users.AsNoTracking()
                join p in db.People on u.PersonId equals (Guid?)p.Id
                where u.Id == issuerId
                select p.FirstName + " " + p.LastName).SingleOrDefaultAsync(ct);

        return TypedResults.Ok(new InviteInfoDto(info.Email!, info.FirstName, info.Level.ToString(), inviter, t.ExpiresAt));
    }

    private static async Task<IResult> AcceptInvite(
        AcceptInviteRequest req, HtnDbContext db, UserManager<AppUser> users, IAccountTokenService tokens, IAuditLog audit, CancellationToken ct)
    {
        const string invalid = "This invite link is invalid or has expired. Ask an admin to send a new one.";
        var token = await tokens.FindUsableAsync(req.Token, AccountTokenPurpose.Invite, ct);
        if (token is null) return Problems.Result(StatusCodes.Status400BadRequest, invalid, code: "invite_invalid");

        var user = await users.FindByIdAsync(token.UserId.ToString());
        if (user is null || !user.IsActive || user.PasswordHash is not null)
            return Problems.Result(StatusCodes.Status400BadRequest, invalid, code: "invite_invalid");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await tokens.TryConsumeAsync(token.Id, ct))
            return Problems.Result(StatusCodes.Status400BadRequest, invalid, code: "invite_invalid");

        var result = await users.AddPasswordAsync(user, req.Password);
        if (!result.Succeeded)
        {
            await tx.RollbackAsync(ct);
            return PasswordProblem(result);
        }

        user.EmailConfirmed = true;
        await users.UpdateAsync(user);
        audit.Record(user.Id, "user.invite_accepted", nameof(AppUser), user.Id.ToString());
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ForgotPassword(
        ForgotPasswordRequest req, HtnDbContext db, UserManager<AppUser> users, IAccountTokenService tokens,
        IAuditLog audit, EmailRateLimiter emailLimits, TimeProvider clock, CancellationToken ct)
    {
        // Counted before the lookup so unknown and existing emails are limited identically.
        if (!emailLimits.TryForgot(req.Email)) return AuthRateLimiting.TooMany();

        var user = await users.FindByEmailAsync(req.Email.Trim());
        if (user is { IsActive: true, PasswordHash: not null, PersonId: { } personId })
        {
            var person = await db.People.AsNoTracking().SingleOrDefaultAsync(p => p.Id == personId && p.IsActive, ct);
            if (person is not null)
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var raw = await tokens.IssueAsync(user.Id, AccountTokenPurpose.PasswordReset, null, ct);
                db.Outbox.Add(OutboxProcessor.PasswordResetEmail(
                    new PasswordResetEmailPayload(user.Id, user.Email!, person.FirstName), raw, clock.GetUtcNow()));
                audit.Record(null, "user.password_reset_requested", nameof(AppUser), user.Id.ToString());
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
        }

        // Always the same response: no account enumeration.
        return TypedResults.Accepted((string?)null);
    }

    private static async Task<IResult> ResetPassword(
        ResetPasswordRequest req, HtnDbContext db, UserManager<AppUser> users, IAccountTokenService tokens, IAuditLog audit, CancellationToken ct)
    {
        const string invalid = "This reset link is invalid or has expired. Request a new one.";
        var token = await tokens.FindUsableAsync(req.Token, AccountTokenPurpose.PasswordReset, ct);
        if (token is null) return Problems.Result(StatusCodes.Status400BadRequest, invalid, code: "reset_invalid");

        var user = await users.FindByIdAsync(token.UserId.ToString());
        if (user is null || !user.IsActive)
            return Problems.Result(StatusCodes.Status400BadRequest, invalid, code: "reset_invalid");
        if (!string.IsNullOrWhiteSpace(req.Email) &&
            !string.Equals(users.NormalizeEmail(req.Email.Trim()), user.NormalizedEmail, StringComparison.Ordinal))
            return Problems.Result(StatusCodes.Status400BadRequest, invalid, code: "reset_invalid");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await tokens.TryConsumeAsync(token.Id, ct))
            return Problems.Result(StatusCodes.Status400BadRequest, invalid, code: "reset_invalid");

        // Identity's reset also rotates the security stamp, which ends other sessions.
        var identityToken = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, identityToken, req.Password);
        if (!result.Succeeded)
        {
            await tx.RollbackAsync(ct);
            return PasswordProblem(result);
        }

        await users.ResetAccessFailedCountAsync(user);
        await users.SetLockoutEndDateAsync(user, null);
        audit.Record(user.Id, "user.password_reset", nameof(AppUser), user.Id.ToString());
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> SetupTwoFactor(HttpContext http, UserManager<AppUser> users, SignInManager<AppUser> signIn)
    {
        var user = await users.GetUserAsync(http.User);
        if (user is null) return TypedResults.Unauthorized();
        if (user.TwoFactorEnabled)
            return Problems.Result(StatusCodes.Status409Conflict, "Two-step verification is already on.", code: "2fa_already_enabled");

        await users.ResetAuthenticatorKeyAsync(user);
        var key = await users.GetAuthenticatorKeyAsync(user) ?? throw new InvalidOperationException("No authenticator key.");
        // Resetting the key rotates the security stamp; re-issue the cookie so this session stays valid.
        await signIn.RefreshSignInAsync(user);

        var uri = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6",
            UrlEncoder.Default.Encode(TotpIssuer), UrlEncoder.Default.Encode(user.Email!), key);
        return TypedResults.Ok(new TwoFactorSetupDto(FormatKey(key), uri));
    }

    private static async Task<IResult> EnableTwoFactor(
        EnableTwoFactorRequest req, HttpContext http, HtnDbContext db, UserManager<AppUser> users, SignInManager<AppUser> signIn,
        IAuditLog audit, CancellationToken ct)
    {
        var user = await users.GetUserAsync(http.User);
        if (user is null) return TypedResults.Unauthorized();
        if (user.TwoFactorEnabled)
            return Problems.Result(StatusCodes.Status409Conflict, "Two-step verification is already on.", code: "2fa_already_enabled");

        var valid = await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, NormalizeCode(req.Code));
        if (!valid) return Problems.Field("code", "The code is not valid. Check your authenticator app and try again.");

        await users.SetTwoFactorEnabledAsync(user, true);
        audit.Record(user.Id, "user.2fa_enabled", nameof(AppUser), user.Id.ToString(),
            new { twoFactorEnabled = false }, new { twoFactorEnabled = true });
        await db.SaveChangesAsync(ct);
        await signIn.RefreshSignInAsync(user);
        return TypedResults.NoContent();
    }

    private static IResult PasswordProblem(IdentityResult result) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            ["password"] = result.Errors.Select(e => e.Description).ToArray(),
        });

    private static string NormalizeCode(string code) => code.Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);

    private static string FormatKey(string key)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < key.Length; i += 4)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(key.AsSpan(i, Math.Min(4, key.Length - i)));
        }
        return sb.ToString().ToLowerInvariant();
    }
}
