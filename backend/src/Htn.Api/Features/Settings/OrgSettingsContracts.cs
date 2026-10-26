using FluentValidation;
using Htn.Domain.Settings;

namespace Htn.Api.Features.Settings;

public sealed record PointPresetsDto(int Small, int Medium, int Large, int Xl);

public sealed record PointsSettingsDto(
    decimal LeadMultiplier, decimal CollabMultiplier, decimal AssistantMultiplier, PointPresetsDto Presets, bool RequireTaskApproval);

public sealed record BrandingDto(string BrandColor, string? LogoLightUrl, string? LogoDarkUrl, int PaymentDeadlineHours);

public sealed record OrgSettingsDto(PointsSettingsDto Points, BrandingDto Branding);

/// <summary>PUT /api/settings/points body: the same shape as <see cref="PointsSettingsDto"/>, with every field required.</summary>
public sealed record UpdatePointsRequest(
    decimal? LeadMultiplier, decimal? CollabMultiplier, decimal? AssistantMultiplier, PointPresetsRequest? Presets, bool? RequireTaskApproval);

public sealed record PointPresetsRequest(int? Small, int? Medium, int? Large, int? Xl);

public sealed record UpdateBrandingRequest(string BrandColor, int? PaymentDeadlineHours);

public sealed class UpdatePointsRequestValidator : AbstractValidator<UpdatePointsRequest>
{
    private const string MultiplierMessage = "Use a value from 0 to 2 in steps of 0.1.";

    public UpdatePointsRequestValidator()
    {
        RuleFor(x => x.LeadMultiplier).NotNull().WithMessage(MultiplierMessage).Must(v => v is null || OrgSettings.IsValidMultiplier(v.Value)).WithMessage(MultiplierMessage);
        RuleFor(x => x.CollabMultiplier).NotNull().WithMessage(MultiplierMessage).Must(v => v is null || OrgSettings.IsValidMultiplier(v.Value)).WithMessage(MultiplierMessage);
        RuleFor(x => x.AssistantMultiplier).NotNull().WithMessage(MultiplierMessage).Must(v => v is null || OrgSettings.IsValidMultiplier(v.Value)).WithMessage(MultiplierMessage);
        RuleFor(x => x.RequireTaskApproval).NotNull().WithMessage("Say whether tasks need approval.");
        RuleFor(x => x.Presets).NotNull().WithMessage("Enter the size presets.").SetValidator(new PointPresetsRequestValidator()!);
    }
}

public sealed class PointPresetsRequestValidator : AbstractValidator<PointPresetsRequest>
{
    private static readonly string Range = $"Use a whole number from {OrgSettings.MinPreset} to {OrgSettings.MaxPreset}.";

    public PointPresetsRequestValidator()
    {
        RuleFor(x => x.Small).NotNull().WithMessage(Range).InclusiveBetween(OrgSettings.MinPreset, OrgSettings.MaxPreset).WithMessage(Range);
        RuleFor(x => x.Medium).NotNull().WithMessage(Range).InclusiveBetween(OrgSettings.MinPreset, OrgSettings.MaxPreset).WithMessage(Range)
            .GreaterThan(x => x.Small).When(x => x.Small is not null, ApplyConditionTo.CurrentValidator).WithMessage("Medium must be more than small.");
        RuleFor(x => x.Large).NotNull().WithMessage(Range).InclusiveBetween(OrgSettings.MinPreset, OrgSettings.MaxPreset).WithMessage(Range)
            .GreaterThan(x => x.Medium).When(x => x.Medium is not null, ApplyConditionTo.CurrentValidator).WithMessage("Large must be more than medium.");
        RuleFor(x => x.Xl).NotNull().WithMessage(Range).InclusiveBetween(OrgSettings.MinPreset, OrgSettings.MaxPreset).WithMessage(Range)
            .GreaterThan(x => x.Large).When(x => x.Large is not null, ApplyConditionTo.CurrentValidator).WithMessage("XL must be more than large.");
    }
}

public sealed class UpdateBrandingRequestValidator : AbstractValidator<UpdateBrandingRequest>
{
    public UpdateBrandingRequestValidator()
    {
        RuleFor(x => x.BrandColor).Must(ColorContrast.IsHex).WithMessage("Use a hex color like #B71C1C.");
        RuleFor(x => x.PaymentDeadlineHours).NotNull().WithMessage("Enter the payment deadline.")
            .InclusiveBetween(OrgSettings.MinPaymentDeadlineHours, OrgSettings.MaxPaymentDeadlineHours)
            .WithMessage($"Use {OrgSettings.MinPaymentDeadlineHours} to {OrgSettings.MaxPaymentDeadlineHours} hours.");
    }
}
