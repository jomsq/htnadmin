using FluentValidation;
using Htn.Domain.Committees;
using Htn.Domain.Common;
using Htn.Domain.Registrations;

namespace Htn.Api.Features.Committees;

public sealed class CommitteeRequestValidator : AbstractValidator<CommitteeRequest>
{
    public CommitteeRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().WithMessage("Enter a committee name.")
            .MaximumLength(Committee.NameMaxLength).WithMessage($"Use at most {Committee.NameMaxLength} characters.");
        RuleFor(r => r.SortOrder).InclusiveBetween(0, Committee.MaxSortOrder)
            .WithMessage($"Sort order must be between 0 and {Committee.MaxSortOrder}.").When(r => r.SortOrder is not null);
    }
}

public sealed class CommitteeMemberRequestValidator : AbstractValidator<CommitteeMemberRequest>
{
    public CommitteeMemberRequestValidator()
    {
        RuleFor(r => r.PersonId).NotEmpty().WithMessage("Choose a person.");
    }
}

/// <summary>A null person id clears the lead, so there is nothing to validate beyond "not the empty GUID".</summary>
public sealed class CommitteeLeadRequestValidator : AbstractValidator<CommitteeLeadRequest>
{
    public CommitteeLeadRequestValidator()
    {
        RuleFor(r => r.PersonId).NotEqual(Guid.Empty).WithMessage("Choose a goon, or clear the lead.")
            .When(r => r.PersonId is not null);
    }
}

public sealed class CommitteeReasonRequestValidator : AbstractValidator<CommitteeReasonRequest>
{
    public CommitteeReasonRequestValidator()
    {
        RuleFor(r => r.Reason).NotEmpty().WithMessage("Say why.")
            .MaximumLength(Committee.ReasonMaxLength).WithMessage($"Use at most {Committee.ReasonMaxLength} characters.");
    }
}

/// <summary>Volunteer names use the same character rules as attendee names (<see cref="PersonNameRules"/>).</summary>
public sealed class VolunteerRequestValidator : AbstractValidator<VolunteerRequest>
{
    public VolunteerRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().WithMessage("Enter the volunteer's name.")
            .MaximumLength(EventVolunteer.NameMaxLength).WithMessage($"Use at most {EventVolunteer.NameMaxLength} characters.")
            .Must(PersonNameRules.IsValidName).WithMessage(PersonNameRules.Message);
        RuleFor(r => r.Email).Must(RegistrationForm.IsEmail).WithMessage("Enter a valid email address.")
            .When(r => !string.IsNullOrWhiteSpace(r.Email));
        RuleFor(r => r.Mobile).Must(m => MobileNumber.Normalize(m) is not null)
            .WithMessage("Enter a Philippine mobile number, e.g. 0917 123 4567.")
            .When(r => !string.IsNullOrWhiteSpace(r.Mobile));
        RuleFor(r => r.CommitteeId).NotEqual(Guid.Empty).WithMessage("Choose a committee, or leave it empty.")
            .When(r => r.CommitteeId is not null);
    }
}
