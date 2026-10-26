using FluentValidation;
using Htn.Domain.Events;

namespace Htn.Api.Features.Events;

public sealed record EventSummaryDto(
    Guid Id, string Slug, string Name, EventStatus Status, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Venue,
    int Capacity, int Sold, string? CoverUrl, bool IsMainEvent);

/// <summary>Summary fields plus details. <see cref="Vouchers"/> is empty for callers without Events.Manage.</summary>
public sealed record EventDetailDto(
    Guid Id, string Slug, string Name, EventStatus Status, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Venue,
    int Capacity, int Sold, string? CoverUrl, bool IsMainEvent,
    string? Description, bool ManualRegistration, bool GroupRegistration, bool PublicPage, ProofMode ProofMode,
    decimal? MembershipFee, int? MembershipYear,
    IReadOnlyList<TicketTypeDto> Tickets, IReadOnlyList<VoucherDto> Vouchers);

public sealed record TicketTypeDto(
    Guid Id, string Name, decimal Price, int Quantity, int Sold, int GroupSize, DateTimeOffset? SalesStartsAt, DateTimeOffset? SalesEndsAt,
    TicketVisibility Visibility, bool IsActive, int SortOrder, TicketStatus Status);

public sealed record VoucherDto(
    Guid Id, string Code, VoucherKind Kind, decimal Value, IReadOnlyList<Guid> TicketTypeIds, int? UsageLimit, int Used,
    DateTimeOffset? ExpiresAt, bool IsActive, VoucherSource Source);

public sealed record CreateEventRequest(
    string Name, string? Slug, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, string Venue, int? Capacity, string? Description)
    : IEventInfoRequest;

/// <summary>
/// PUT body. Name, dates, venue and capacity are required. A null slug or null setting keeps the current value;
/// a null description clears it.
/// </summary>
public sealed record UpdateEventRequest(
    string Name, string? Slug, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, string Venue, int? Capacity, string? Description,
    bool? ManualRegistration, bool? GroupRegistration, bool? PublicPage, ProofMode? ProofMode,
    bool? IsMainEvent, decimal? MembershipFee, int? MembershipYear) : IEventInfoRequest;

/// <summary>Ticket type body for create and update. A null group size means 1; a null visibility means Public.</summary>
public sealed record TicketTypeRequest(
    string Name, decimal? Price, int? Quantity, int? GroupSize, DateTimeOffset? SalesStartsAt, DateTimeOffset? SalesEndsAt,
    TicketVisibility? Visibility);

/// <summary>Voucher body for create and update. Empty or null <see cref="TicketTypeIds"/> = every ticket type.</summary>
public sealed record VoucherRequest(
    string Code, VoucherKind? Kind, decimal? Value, IReadOnlyList<Guid>? TicketTypeIds, int? UsageLimit, DateTimeOffset? ExpiresAt);

/// <summary>The event-info fields shared by create and update.</summary>
public interface IEventInfoRequest
{
    string Name { get; }
    string? Slug { get; }
    DateTimeOffset? StartsAt { get; }
    DateTimeOffset? EndsAt { get; }
    string Venue { get; }
    int? Capacity { get; }
    string? Description { get; }
}

internal static class EventRules
{
    public const string SlugMessage = "Use 3–60 lower-case letters and digits, with single dashes between words.";

    public static bool HasCentavosAtMost(decimal? value) => value is not { } v || decimal.Round(v, 2) == v;
}

public sealed class EventInfoValidator : AbstractValidator<IEventInfoRequest>
{
    public EventInfoValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Enter the event name.")
            .MaximumLength(Event.NameMaxLength).WithMessage($"Use at most {Event.NameMaxLength} characters.");
        RuleFor(x => x.Slug).Must(s => s is null || EventSlug.IsValid(s)).WithMessage(EventRules.SlugMessage);
        RuleFor(x => x.StartsAt).NotNull().WithMessage("Enter the start date and time.");
        RuleFor(x => x.EndsAt).NotNull().WithMessage("Enter the end date and time.")
            .Must((x, end) => end is null || x.StartsAt is not { } start || end > start).WithMessage("The end must be after the start.");
        RuleFor(x => x.Venue).NotEmpty().WithMessage("Enter the venue.")
            .MaximumLength(Event.VenueMaxLength).WithMessage($"Use at most {Event.VenueMaxLength} characters.");
        RuleFor(x => x.Capacity).NotNull().WithMessage("Enter the capacity.")
            .InclusiveBetween(Event.MinCapacity, Event.MaxCapacity).WithMessage($"Use {Event.MinCapacity} to {Event.MaxCapacity:N0}.");
        RuleFor(x => x.Description).MaximumLength(Event.DescriptionMaxLength).WithMessage($"Use at most {Event.DescriptionMaxLength} characters.");
    }
}

public sealed class CreateEventRequestValidator : AbstractValidator<CreateEventRequest>
{
    public CreateEventRequestValidator() => Include(new EventInfoValidator());
}

public sealed class UpdateEventRequestValidator : AbstractValidator<UpdateEventRequest>
{
    public UpdateEventRequestValidator()
    {
        Include(new EventInfoValidator());
        RuleFor(x => x.ProofMode).IsInEnum().WithMessage("Choose upload or reply by email.");
        RuleFor(x => x.MembershipFee)
            .InclusiveBetween(0m, Event.MaxMembershipFee).WithMessage($"Use ₱0 to ₱{Event.MaxMembershipFee:N0}.")
            .Must(EventRules.HasCentavosAtMost).WithMessage("Use at most 2 decimal places.");
        RuleFor(x => x.MembershipYear)
            .InclusiveBetween(Event.MinMembershipYear, Event.MaxMembershipYear).WithMessage("Enter a year like 2026.");
    }
}

public sealed class TicketTypeRequestValidator : AbstractValidator<TicketTypeRequest>
{
    public TicketTypeRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Enter the ticket name.")
            .MaximumLength(TicketType.NameMaxLength).WithMessage($"Use at most {TicketType.NameMaxLength} characters.");
        RuleFor(x => x.Price).NotNull().WithMessage("Enter the price (0 for free).")
            .InclusiveBetween(0m, TicketType.MaxPrice).WithMessage("Use ₱0 to ₱1,000,000.")
            .Must(EventRules.HasCentavosAtMost).WithMessage("Use at most 2 decimal places.");
        RuleFor(x => x.Quantity).NotNull().WithMessage("Enter the quantity.")
            .InclusiveBetween(TicketType.MinQuantity, TicketType.MaxQuantity).WithMessage($"Use {TicketType.MinQuantity} to {TicketType.MaxQuantity:N0}.");
        RuleFor(x => x.GroupSize)
            .InclusiveBetween(TicketType.MinGroupSize, TicketType.MaxGroupSize).WithMessage($"Use {TicketType.MinGroupSize} to {TicketType.MaxGroupSize} people.");
        RuleFor(x => x.GroupSize)
            .LessThanOrEqualTo(TicketType.MaxPublicGroupSize).When(x => (x.Visibility ?? TicketVisibility.Public) == TicketVisibility.Public)
            .WithMessage($"A public ticket has at most {TicketType.MaxPublicGroupSize} people. Make it hidden for larger groups.");
        RuleFor(x => x.SalesEndsAt).Must((x, end) => end is null || x.SalesStartsAt is not { } start || end > start)
            .WithMessage("Sales must end after they start.");
        RuleFor(x => x.Visibility).IsInEnum().WithMessage("Choose public or hidden.");
    }
}

public sealed class VoucherRequestValidator : AbstractValidator<VoucherRequest>
{
    public VoucherRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().WithMessage("Enter a code.")
            .Must(Voucher.IsValidCode).WithMessage("Use 3–30 letters, digits and dashes.");
        RuleFor(x => x.Kind).NotNull().WithMessage("Choose percent or fixed amount.").IsInEnum().WithMessage("Choose percent or fixed amount.");
        RuleFor(x => x.Value).NotNull().WithMessage("Enter the discount.");
        RuleFor(x => x.Value).Must((x, value) => Voucher.IsValidValue(x.Kind!.Value, value!.Value))
            .When(x => x.Value is not null && x.Kind is { } k && Enum.IsDefined(k))
            .WithMessage(x => x.Kind == VoucherKind.Percent
                ? "Use 1 to 100 percent (at most 2 decimal places)."
                : "Use more than ₱0, up to ₱1,000,000 (at most 2 decimal places).");
        RuleFor(x => x.UsageLimit)
            .InclusiveBetween(1, Voucher.MaxUsageLimit).WithMessage($"Use 1 to {Voucher.MaxUsageLimit:N0}, or leave it empty for no limit.");
        RuleFor(x => x.TicketTypeIds)
            .Must(ids => ids is null || ids.Count <= Voucher.MaxTicketScope).WithMessage($"Choose at most {Voucher.MaxTicketScope} ticket types.")
            .Must(ids => ids is null || !ids.Contains(Guid.Empty)).WithMessage("Choose valid ticket types.");
    }
}
