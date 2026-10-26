namespace Htn.Api.Features.Auth;

public sealed record AntiforgeryTokenDto(string Token);

public sealed record LoginRequest(string Email, string Password, bool? RememberMe);
public sealed record LoginTwoFactorRequest(string Code, bool? RememberMe);
public sealed record LoginChallengeDto(bool Requires2fa);

public sealed record InviteInfoDto(string Email, string FirstName, string Level, string? InviterName, DateTimeOffset ExpiresAt);

public sealed record AcceptInviteRequest(string Token, string Password);
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Token, string? Email, string Password);

public sealed record TwoFactorSetupDto(string SharedKey, string AuthenticatorUri);
public sealed record EnableTwoFactorRequest(string Code);

public sealed record PreferencesDto(string Mode, string TextSize, string Density);

public sealed record MeDto(
    Guid Id,
    Guid? PersonId,
    string FirstName,
    string LastName,
    string Email,
    string? Level,
    bool IsCfo,
    bool TwoFactorEnabled,
    bool TwoFactorRequired,
    IReadOnlyList<string> Policies,
    PreferencesDto Preferences);
