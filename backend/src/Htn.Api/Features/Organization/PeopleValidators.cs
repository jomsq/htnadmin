using FluentValidation;
using Htn.Domain.Organization;

namespace Htn.Api.Features.Organization;

public sealed class CreatePersonRequestValidator : AbstractValidator<CreatePersonRequest>
{
    public CreatePersonRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().WithMessage("Enter a first name.")
            .MaximumLength(Person.NameMaxLength).WithMessage($"Use at most {Person.NameMaxLength} characters.");
        RuleFor(x => x.MiddleName).MaximumLength(Person.NameMaxLength).WithMessage($"Use at most {Person.NameMaxLength} characters.");
        RuleFor(x => x.LastName).NotEmpty().WithMessage("Enter a last name.")
            .MaximumLength(Person.NameMaxLength).WithMessage($"Use at most {Person.NameMaxLength} characters.");
        RuleFor(x => x.Email).NotEmpty().WithMessage("Enter an email.")
            .EmailAddress().WithMessage("Enter a valid email.")
            .MaximumLength(Person.EmailMaxLength).WithMessage($"Use at most {Person.EmailMaxLength} characters.");
        RuleFor(x => x.Level).NotNull().WithMessage("Choose a level.")
            .IsInEnum().WithMessage("Choose a level.");
        RuleFor(x => x.Position).MaximumLength(Person.PositionMaxLength).WithMessage($"Use at most {Person.PositionMaxLength} characters.");
    }
}

public sealed class DeactivatePersonRequestValidator : AbstractValidator<DeactivatePersonRequest>
{
    public DeactivatePersonRequestValidator()
    {
        RuleFor(x => x.Reason).MaximumLength(Person.ReasonMaxLength).WithMessage($"Use at most {Person.ReasonMaxLength} characters.");
    }
}
