using FluentValidation;

namespace Htn.Api.Features.Auth;

public static class PasswordRules
{
    public const int MinLength = 12;
    public const int MaxLength = 128;

    public static IRuleBuilderOptions<T, string> NewPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Enter a password.")
            .MinimumLength(MinLength).WithMessage($"Use at least {MinLength} characters.")
            .MaximumLength(MaxLength).WithMessage($"Use at most {MaxLength} characters.");
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("Enter your email.").MaximumLength(254);
        RuleFor(x => x.Password).NotEmpty().WithMessage("Enter your password.").MaximumLength(PasswordRules.MaxLength);
    }
}

public sealed class LoginTwoFactorRequestValidator : AbstractValidator<LoginTwoFactorRequest>
{
    public LoginTwoFactorRequestValidator() =>
        RuleFor(x => x.Code).NotEmpty().WithMessage("Enter the 6-digit code.").MaximumLength(20);
}

public sealed class AcceptInviteRequestValidator : AbstractValidator<AcceptInviteRequest>
{
    public AcceptInviteRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty().WithMessage("The invite link is incomplete.").MaximumLength(200);
        RuleFor(x => x.Password).NewPassword();
    }
}

public sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator() =>
        RuleFor(x => x.Email).NotEmpty().WithMessage("Enter your email.").EmailAddress().WithMessage("Enter a valid email.").MaximumLength(254);
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty().WithMessage("The reset link is incomplete.").MaximumLength(200);
        RuleFor(x => x.Email).MaximumLength(254);
        RuleFor(x => x.Password).NewPassword();
    }
}

public sealed class EnableTwoFactorRequestValidator : AbstractValidator<EnableTwoFactorRequest>
{
    public EnableTwoFactorRequestValidator() =>
        RuleFor(x => x.Code).NotEmpty().WithMessage("Enter the 6-digit code.").MaximumLength(20);
}
