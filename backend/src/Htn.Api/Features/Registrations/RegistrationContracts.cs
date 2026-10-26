using FluentValidation;
using Htn.Domain.Events;
using Htn.Domain.Registrations;

namespace Htn.Api.Features.Registrations;

// ---------- Requests ----------

/// <summary>One person on the form. The buyer (first) needs gender, email and mobile; group members need names and email.</summary>
public sealed record AttendeeInput(
    string? FirstName, string? MiddleName, string? LastName, string? Gender, string? Email, string? Mobile,
    string? Occupation, string? Company, string? ShirtSize, string? HearAbout);

/// <summary>Public checkout. <see cref="Website"/> is a honeypot and must be empty.</summary>
public sealed record PublicRegistrationRequest(
    Guid? IdempotencyKey, Guid? TicketTypeId, string? VoucherCode, IReadOnlyList<AttendeeInput>? Attendees, bool? Consent, string? Website)
    : IRegistrationForm;

public sealed record VoucherCheckRequest(string? Code, Guid? TicketTypeId);

public sealed record MarkPaidInput(PaymentMethod? Method, Guid? PaymentAccountId, string? Reference, DateTimeOffset? ReceivedAt);

/// <summary>Manual registration by an admin: same form, hidden tickets allowed, optionally marked paid at once.</summary>
public sealed record ManualRegistrationRequest(
    Guid? TicketTypeId, string? VoucherCode, IReadOnlyList<AttendeeInput>? Attendees, bool? Consent, MarkPaidInput? MarkPaid)
    : IRegistrationForm;

public sealed record BulkMarkPaidRequest(
    IReadOnlyList<Guid>? Ids, PaymentMethod? Method, Guid? PaymentAccountId, string? Reference, DateTimeOffset? ReceivedAt);

public sealed record ReasonRequest(string? Reason);

public interface IRegistrationForm
{
    Guid? TicketTypeId { get; }
    string? VoucherCode { get; }
    IReadOnlyList<AttendeeInput>? Attendees { get; }
    bool? Consent { get; }
}

// ---------- Public responses ----------

public sealed record PublicTicketDto(Guid Id, string Name, decimal Price, int GroupSize, int Remaining, string? Note);

public sealed record PublicEventDto(
    string Name, string Slug, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Venue, string? Description, string? CoverUrl,
    string? LogoUrl, string BrandColor, bool GroupRegistration, ProofMode ProofMode, IReadOnlyList<PublicTicketDto> Tickets,
    IReadOnlyList<string> Genders, IReadOnlyList<string> HearAbout, IReadOnlyList<string> ShirtSizes, string ConsentText, string ConsentVersion);

public sealed record VoucherCheckDto(bool Valid, decimal Discount, decimal Total);

/// <summary>Only what the success screen shows. <see cref="Deadline"/> is null when nothing is due (status Paid).</summary>
public sealed record RegistrationCreatedDto(
    IReadOnlyList<string> ParticipantIds, decimal AmountDue, DateTimeOffset? Deadline, ProofMode ProofMode, string Email, RegistrationStatus Status);

public sealed record ProofInfoDto(
    string EventName, string ParticipantId, decimal AmountDue, DateTimeOffset? Deadline, bool Uploaded, RegistrationStatus Status);

// ---------- Admin responses ----------

public sealed record RegistrationCountsDto(int All, int PendingPayment, int Paid, int CheckedIn, int Cancelled, int Overdue);

public sealed record RegistrationRowDto(
    Guid Id, string BuyerName, string BuyerEmail, int AttendeeCount, IReadOnlyList<string> ParticipantIds, string TicketName,
    decimal Total, RegistrationStatus Status, bool Overdue, PaymentMethod? Method, string? Reference, bool HasProof,
    DateTimeOffset CreatedAt, RegistrationSource Source);

public sealed record RegistrationListDto(
    IReadOnlyList<RegistrationRowDto> Items, int Total, int Page, int PageSize, RegistrationCountsDto Counts);

public sealed record AttendeeDto(
    Guid Id, string ParticipantId, string FirstName, string? MiddleName, string LastName, string? Gender, string Email, string? Mobile,
    string? Occupation, string? Company, string? ShirtSize, string? HearAbout, bool IsBuyer, DateTimeOffset? CheckedInAt, string? CheckedInStation);

public sealed record PaymentDto(
    Guid Id, PaymentMethod Method, Guid? PaymentAccountId, string? PaymentAccountLabel, string? Reference, decimal Amount,
    DateTimeOffset ReceivedAt, DateTimeOffset MarkedAt, string? MarkedByName);

public sealed record ProofDto(string? Reference, DateTimeOffset UploadedAt, string Url);

/// <summary><see cref="Kind"/> is the audit action without the "registration." prefix (create, mark_paid, cancel, …).</summary>
public sealed record HistoryDto(DateTimeOffset At, string Kind, string? ActorName, string? Reason);

public sealed record RegistrationDetailDto(
    Guid Id, string BuyerName, string BuyerEmail, int AttendeeCount, IReadOnlyList<string> ParticipantIds, string TicketName,
    decimal Total, RegistrationStatus Status, bool Overdue, PaymentMethod? Method, string? Reference, bool HasProof,
    DateTimeOffset CreatedAt, RegistrationSource Source,
    Guid TicketTypeId, IReadOnlyList<AttendeeDto> Attendees, decimal Subtotal, decimal Discount, string? VoucherCode, DateTimeOffset? Deadline,
    PaymentDto? Payment, ProofDto? Proof, string? CancelReason, DateTimeOffset ConsentAt, string ConsentVersion,
    IReadOnlyList<HistoryDto> History);

public sealed record MarkPaidResultDto(int Updated);

// ---------- Validators ----------

internal static class RegistrationRules
{
    public const int MaxBulkIds = 200;

    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static AttendeeDetails ToDetails(AttendeeInput a) => new(
        a.FirstName!.Trim(), Clean(a.MiddleName), a.LastName!.Trim(), Clean(a.Gender), a.Email!.Trim(),
        Clean(a.Mobile) is { } m ? MobileNumber.Normalize(m) : null,
        Clean(a.Occupation), Clean(a.Company), Clean(a.ShirtSize), Clean(a.HearAbout));
}

public abstract class AttendeeInputValidator : AbstractValidator<AttendeeInput>
{
    protected AttendeeInputValidator(bool buyer)
    {
        RuleFor(x => x.FirstName).NotEmpty().WithMessage("Enter the first name.")
            .MaximumLength(RegistrationForm.NameMaxLength).WithMessage($"Use at most {RegistrationForm.NameMaxLength} characters.");
        RuleFor(x => x.MiddleName).MaximumLength(RegistrationForm.NameMaxLength).WithMessage($"Use at most {RegistrationForm.NameMaxLength} characters.");
        RuleFor(x => x.LastName).NotEmpty().WithMessage("Enter the last name.")
            .MaximumLength(RegistrationForm.NameMaxLength).WithMessage($"Use at most {RegistrationForm.NameMaxLength} characters.");
        RuleFor(x => x.Email).NotEmpty().WithMessage("Enter the email address.")
            .Must(RegistrationForm.IsEmail).When(x => !string.IsNullOrWhiteSpace(x.Email), ApplyConditionTo.CurrentValidator)
            .WithMessage("Enter a valid email address.");
        RuleFor(x => x.Gender).Must(g => RegistrationRules.Clean(g) is not { } v || RegistrationForm.Genders.Contains(v))
            .WithMessage("Choose an option from the list.");
        RuleFor(x => x.Mobile).Must(m => RegistrationRules.Clean(m) is not { } v || MobileNumber.Normalize(v) is not null)
            .WithMessage("Enter a Philippine mobile number, like 917 123 4567.");
        RuleFor(x => x.Occupation).MaximumLength(RegistrationForm.OccupationMaxLength).WithMessage($"Use at most {RegistrationForm.OccupationMaxLength} characters.");
        RuleFor(x => x.Company).MaximumLength(RegistrationForm.CompanyMaxLength).WithMessage($"Use at most {RegistrationForm.CompanyMaxLength} characters.");
        RuleFor(x => x.ShirtSize).Must(s => RegistrationRules.Clean(s) is not { } v || RegistrationForm.ShirtSizes.Contains(v))
            .WithMessage("Choose a shirt size from the list.");
        RuleFor(x => x.HearAbout).Must(h => RegistrationRules.Clean(h) is not { } v || RegistrationForm.HearAbout.Contains(v))
            .WithMessage("Choose an option from the list.");
        if (buyer)
        {
            RuleFor(x => x.Gender).NotEmpty().WithMessage("Choose an option.");
            RuleFor(x => x.Mobile).NotEmpty().WithMessage("Enter your mobile number.");
        }
    }
}

/// <summary>The first attendee: also needs gender and mobile.</summary>
public sealed class BuyerInputValidator() : AttendeeInputValidator(buyer: true);

public sealed class GroupMemberInputValidator() : AttendeeInputValidator(buyer: false);

public sealed class RegistrationFormValidator : AbstractValidator<IRegistrationForm>
{
    public RegistrationFormValidator()
    {
        AttendeeInputValidator buyer = new BuyerInputValidator();
        var member = new GroupMemberInputValidator();
        RuleFor(x => x.TicketTypeId).NotNull().WithMessage("Choose a ticket.");
        RuleFor(x => x.VoucherCode).MaximumLength(Voucher.CodeMaxLength).WithMessage("This voucher code is not valid.");
        RuleFor(x => x.Attendees).NotEmpty().WithMessage("Enter the attendee details.")
            .Must(a => a is null || a.Count <= TicketType.MaxGroupSize).WithMessage($"A group has at most {TicketType.MaxGroupSize} people.");
        // The buyer (index 0) has more required fields than group members, so validate each with the right rules and
        // report errors as attendees[i].field.
        RuleFor(x => x.Attendees).Custom((list, ctx) =>
        {
            if (list is not { Count: > 0 and <= TicketType.MaxGroupSize }) return;
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] is not { } a)
                {
                    ctx.AddFailure($"attendees[{i}]", "Enter the attendee details.");
                    continue;
                }
                foreach (var error in (i == 0 ? buyer : member).Validate(a).Errors)
                    ctx.AddFailure($"attendees[{i}].{error.PropertyName}", error.ErrorMessage);
            }
        });
        RuleFor(x => x.Consent).Equal(true).WithMessage("Agree to the Data Privacy Act consent to continue.");
    }
}

public sealed class PublicRegistrationRequestValidator : AbstractValidator<PublicRegistrationRequest>
{
    public PublicRegistrationRequestValidator()
    {
        Include(new RegistrationFormValidator());
        RuleFor(x => x.IdempotencyKey).NotNull().WithMessage("Missing request key. Refresh the page and try again.")
            .NotEqual(Guid.Empty).WithMessage("Missing request key. Refresh the page and try again.");
    }
}

public sealed class MarkPaidInputValidator : AbstractValidator<MarkPaidInput>
{
    public MarkPaidInputValidator()
    {
        RuleFor(x => x.Method).NotNull().WithMessage("Choose how it was paid.").IsInEnum().WithMessage("Choose how it was paid.");
        RuleFor(x => x.Reference).MaximumLength(Payment.ReferenceMaxLength).WithMessage($"Use at most {Payment.ReferenceMaxLength} characters.");
        RuleFor(x => x.PaymentAccountId).Null().When(x => x.Method == PaymentMethod.Comp).WithMessage("A comp has no payment account.");
    }
}

public sealed class ManualRegistrationRequestValidator : AbstractValidator<ManualRegistrationRequest>
{
    public ManualRegistrationRequestValidator()
    {
        Include(new RegistrationFormValidator());
        RuleFor(x => x.MarkPaid!).SetValidator(new MarkPaidInputValidator()).When(x => x.MarkPaid is not null);
    }
}

public sealed class BulkMarkPaidRequestValidator : AbstractValidator<BulkMarkPaidRequest>
{
    public BulkMarkPaidRequestValidator()
    {
        RuleFor(x => x.Ids).NotEmpty().WithMessage("Select at least one registration.")
            .Must(ids => ids is null || ids.Count <= RegistrationRules.MaxBulkIds).WithMessage($"Select at most {RegistrationRules.MaxBulkIds} at a time.");
        RuleFor(x => x.Method).NotNull().WithMessage("Choose how it was paid.").IsInEnum().WithMessage("Choose how it was paid.");
        RuleFor(x => x.Reference).MaximumLength(Payment.ReferenceMaxLength).WithMessage($"Use at most {Payment.ReferenceMaxLength} characters.");
        RuleFor(x => x.PaymentAccountId).Null().When(x => x.Method == PaymentMethod.Comp).WithMessage("A comp has no payment account.");
    }
}

public sealed class VoucherCheckRequestValidator : AbstractValidator<VoucherCheckRequest>
{
    public VoucherCheckRequestValidator()
    {
        // Shape only: an unknown or malformed code still gets { valid: false } from the endpoint, never a reason.
        RuleFor(x => x.Code).MaximumLength(100).WithMessage("This voucher code is not valid.");
    }
}

public sealed class ReasonRequestValidator : AbstractValidator<ReasonRequest>
{
    public ReasonRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Give a reason.")
            .MaximumLength(RegistrationForm.ReasonMaxLength).WithMessage($"Use at most {RegistrationForm.ReasonMaxLength} characters.");
    }
}
