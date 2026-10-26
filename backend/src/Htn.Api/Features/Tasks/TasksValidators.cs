using FluentValidation;
using Htn.Domain.Tasks;

namespace Htn.Api.Features.Tasks;

internal static class TaskValidationRules
{
    public static IRuleBuilderOptions<T, string> Title<T>(IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Enter a title.")
            .MaximumLength(TaskItem.TitleMaxLength).WithMessage($"Use at most {TaskItem.TitleMaxLength} characters.");

    public static IRuleBuilderOptions<T, IReadOnlyList<string>?> Labels<T>(IRuleBuilder<T, IReadOnlyList<string>?> rule) =>
        rule.Must(l => l is null || l.Count <= TaskItem.MaxLabels)
            .WithMessage($"A task can have at most {TaskItem.MaxLabels} labels.")
            .Must(l => l is null || l.All(x => (x ?? "").Trim().Length <= TaskItem.LabelMaxLength))
            .WithMessage($"A label can be at most {TaskItem.LabelMaxLength} characters.");

    public static IRuleBuilderOptions<T, int?> BasePoints<T>(IRuleBuilder<T, int?> rule) =>
        rule.InclusiveBetween(0, TaskItem.MaxBasePoints)
            .WithMessage($"Base points must be between 0 and {TaskItem.MaxBasePoints}.");

    public static IRuleBuilderOptions<T, string> Reason<T>(IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Say why.")
            .MaximumLength(TaskItem.ReasonMaxLength).WithMessage($"Use at most {TaskItem.ReasonMaxLength} characters.");
}

public sealed class CreateTaskRequestValidator : AbstractValidator<CreateTaskRequest>
{
    public CreateTaskRequestValidator()
    {
        RuleFor(r => r.CommitteeId).NotEmpty().WithMessage("Choose a committee.");
        TaskValidationRules.Title(RuleFor(r => r.Title));
        RuleFor(r => r.Description).MaximumLength(TaskItem.DescriptionMaxLength)
            .WithMessage($"Use at most {TaskItem.DescriptionMaxLength} characters.");
        TaskValidationRules.Labels(RuleFor(r => r.Labels));
        TaskValidationRules.BasePoints(RuleFor(r => r.BasePoints)).When(r => r.BasePoints is not null);
        RuleFor(r => r.Status).IsInEnum().WithMessage("Unknown column.").When(r => r.Status is not null);
        RuleFor(r => r.Status).NotEqual(TaskBoardStatus.Done).WithMessage("A new task can't start in Done.")
            .When(r => r.Status is not null);
    }
}

public sealed class UpdateTaskRequestValidator : AbstractValidator<UpdateTaskRequest>
{
    public UpdateTaskRequestValidator()
    {
        TaskValidationRules.Title(RuleFor(r => r.Title));
        RuleFor(r => r.Description).MaximumLength(TaskItem.DescriptionMaxLength)
            .WithMessage($"Use at most {TaskItem.DescriptionMaxLength} characters.");
        TaskValidationRules.Labels(RuleFor(r => r.Labels));
        TaskValidationRules.BasePoints(RuleFor(r => r.BasePoints)).When(r => r.BasePoints is not null);
        RuleFor(r => r.CommitteeId).NotEqual(Guid.Empty).WithMessage("Choose a committee.").When(r => r.CommitteeId is not null);
    }
}

public sealed class MoveTaskRequestValidator : AbstractValidator<MoveTaskRequest>
{
    public MoveTaskRequestValidator()
    {
        RuleFor(r => r.Status).NotNull().WithMessage("Choose a column.").IsInEnum().WithMessage("Unknown column.");
        RuleFor(r => r.SortOrder).InclusiveBetween(0, TaskItem.MaxSortOrder)
            .WithMessage($"Sort order must be between 0 and {TaskItem.MaxSortOrder}.").When(r => r.SortOrder is not null);
    }
}

public sealed class ReopenTaskRequestValidator : AbstractValidator<ReopenTaskRequest>
{
    public ReopenTaskRequestValidator()
    {
        TaskValidationRules.Reason(RuleFor(r => r.Reason));
        RuleFor(r => r.Status).IsInEnum().WithMessage("Unknown column.").When(r => r.Status is not null);
        RuleFor(r => r.Status).NotEqual(TaskBoardStatus.Done).WithMessage("Choose a column other than Done.")
            .When(r => r.Status is not null);
    }
}

public sealed class TaskReasonRequestValidator : AbstractValidator<TaskReasonRequest>
{
    public TaskReasonRequestValidator() => TaskValidationRules.Reason(RuleFor(r => r.Reason));
}

public sealed class AddAssigneeRequestValidator : AbstractValidator<AddAssigneeRequest>
{
    public AddAssigneeRequestValidator()
    {
        RuleFor(r => r.Role).NotNull().WithMessage("Choose a role.").IsInEnum().WithMessage("Unknown role.");
        // Exactly one of the two: a person with a login, or a volunteer.
        RuleFor(r => r.PersonId)
            .Must((r, _) => (r.PersonId is not null) ^ (r.VolunteerId is not null))
            .WithMessage("Choose either a person or a volunteer.");
        RuleFor(r => r.PersonId).NotEqual(Guid.Empty).WithMessage("Choose a person.").When(r => r.PersonId is not null);
        RuleFor(r => r.VolunteerId).NotEqual(Guid.Empty).WithMessage("Choose a volunteer.").When(r => r.VolunteerId is not null);
        // The volunteer-must-be-an-Assistant rule lives in the handler, so the UI gets the `volunteer_role` code.
    }
}

public sealed class ChecklistItemRequestValidator : AbstractValidator<ChecklistItemRequest>
{
    public ChecklistItemRequestValidator() =>
        RuleFor(r => r.Text).NotEmpty().WithMessage("Enter the step.")
            .MaximumLength(TaskChecklistItem.TextMaxLength).WithMessage($"Use at most {TaskChecklistItem.TextMaxLength} characters.");
}

public sealed class UpdateChecklistItemRequestValidator : AbstractValidator<UpdateChecklistItemRequest>
{
    public UpdateChecklistItemRequestValidator()
    {
        RuleFor(r => r.Text).NotEmpty().WithMessage("Enter the step.")
            .MaximumLength(TaskChecklistItem.TextMaxLength).WithMessage($"Use at most {TaskChecklistItem.TextMaxLength} characters.")
            .When(r => r.Text is not null);
        RuleFor(r => r.IsDone).NotNull().WithMessage("Nothing to change.").When(r => r.Text is null);
    }
}

public sealed class ReorderChecklistRequestValidator : AbstractValidator<ReorderChecklistRequest>
{
    public ReorderChecklistRequestValidator() =>
        RuleFor(r => r.ItemIds).NotEmpty().WithMessage("Send the new order.")
            .Must(ids => ids is null || ids.Count <= TaskChecklistItem.MaxItems)
            .WithMessage($"A checklist can have at most {TaskChecklistItem.MaxItems} steps.")
            .Must(ids => ids is null || ids.Distinct().Count() == ids.Count).WithMessage("Each step can only appear once.");
}

public sealed class CreateCommentRequestValidator : AbstractValidator<CreateCommentRequest>
{
    public CreateCommentRequestValidator()
    {
        RuleFor(r => r.Body).NotEmpty().WithMessage("Write a comment.")
            .MaximumLength(TaskComment.BodyMaxLength).WithMessage($"Use at most {TaskComment.BodyMaxLength} characters.");
        RuleFor(r => r.MentionPersonIds).Must(ids => ids is null || ids.Distinct().Count() <= TaskComment.MaxMentions)
            .WithMessage($"A comment can mention at most {TaskComment.MaxMentions} people.");
    }
}

public sealed class UpdateCommentRequestValidator : AbstractValidator<UpdateCommentRequest>
{
    public UpdateCommentRequestValidator() =>
        RuleFor(r => r.Body).NotEmpty().WithMessage("Write a comment.")
            .MaximumLength(TaskComment.BodyMaxLength).WithMessage($"Use at most {TaskComment.BodyMaxLength} characters.");
}
