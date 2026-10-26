using FluentValidation;
using Htn.Domain.CheckIn;

namespace Htn.Api.Features.CheckIn;

internal static class StationRules
{
    public const string Message = "Enter a station name of at most 40 characters.";

    public static IRuleBuilderOptions<T, string?> ValidStation<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(s => CheckInStation.Normalize(s) is not null).WithMessage(Message);
}

public sealed class ScanRequestValidator : AbstractValidator<ScanRequest>
{
    /// <summary>A real QR token is about 140 characters; anything far beyond that is refused before any work.</summary>
    public const int PayloadMaxLength = 512;

    public ScanRequestValidator()
    {
        RuleFor(r => r.Payload).NotEmpty().WithMessage("Scan a ticket or type a participant ID.")
            .MaximumLength(PayloadMaxLength).WithMessage("That code is too long to be a ticket.");
        RuleFor(r => r.Station).ValidStation();
    }
}

public sealed class ManualCheckInRequestValidator : AbstractValidator<ManualCheckInRequest>
{
    public ManualCheckInRequestValidator()
    {
        RuleFor(r => r.AttendeeId).NotEmpty().WithMessage("Choose an attendee.");
        RuleFor(r => r.Station).ValidStation();
    }
}
