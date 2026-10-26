using FluentValidation;
using Htn.Domain.Organization;

namespace Htn.Api.Features.Organization;

public sealed record PositionDto(Guid Id, PositionKind Kind, string Title, int SortOrder, bool IsActive, PersonRefDto? Holder);

public sealed record CreatePositionRequest(PositionKind? Kind, string Title);

public sealed record UpdatePositionRequest(string Title, bool? IsActive);

public sealed record AssignPositionRequest(Guid? PersonId);

public sealed record VacatePositionRequest(string? Reason);

public sealed class CreatePositionRequestValidator : AbstractValidator<CreatePositionRequest>
{
    public CreatePositionRequestValidator()
    {
        RuleFor(x => x.Kind).NotNull().WithMessage("Choose board seat or goon position.").IsInEnum().WithMessage("Choose board seat or goon position.");
        RuleFor(x => x.Title).NotEmpty().WithMessage("Enter a title.")
            .MaximumLength(Position.TitleMaxLength).WithMessage($"Use at most {Position.TitleMaxLength} characters.");
    }
}

public sealed class UpdatePositionRequestValidator : AbstractValidator<UpdatePositionRequest>
{
    public UpdatePositionRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().WithMessage("Enter a title.")
            .MaximumLength(Position.TitleMaxLength).WithMessage($"Use at most {Position.TitleMaxLength} characters.");
        RuleFor(x => x.IsActive).NotNull().WithMessage("Say whether the position is active.");
    }
}

public sealed class AssignPositionRequestValidator : AbstractValidator<AssignPositionRequest>
{
    public AssignPositionRequestValidator()
    {
        RuleFor(x => x.PersonId).NotNull().WithMessage("Choose a person.").NotEqual(Guid.Empty).WithMessage("Choose a person.");
    }
}

public sealed class VacatePositionRequestValidator : AbstractValidator<VacatePositionRequest>
{
    public VacatePositionRequestValidator()
    {
        RuleFor(x => x.Reason).MaximumLength(Position.ReasonMaxLength).WithMessage($"Use at most {Position.ReasonMaxLength} characters.");
    }
}
