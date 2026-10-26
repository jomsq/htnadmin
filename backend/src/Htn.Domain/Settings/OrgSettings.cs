using Htn.Domain.Common;

namespace Htn.Domain.Settings;

public enum LogoVariant
{
    Light,
    Dark,
}

/// <summary>Organization-wide settings. Exactly one row (<see cref="SingletonId"/>), seeded by the migration.</summary>
public sealed class OrgSettings
{
    public const int SingletonId = 1;
    public const string DefaultBrandColor = "#B71C1C";
    public const decimal MaxMultiplier = 2.0m;
    public const int MinPreset = 1;
    public const int MaxPreset = 1000;
    public const int MinPaymentDeadlineHours = 1;
    public const int MaxPaymentDeadlineHours = 336;

    private OrgSettings() { }

    public int Id { get; private set; }

    public decimal LeadMultiplier { get; private set; }
    public decimal CollabMultiplier { get; private set; }
    public decimal AssistantMultiplier { get; private set; }
    public int PresetSmall { get; private set; }
    public int PresetMedium { get; private set; }
    public int PresetLarge { get; private set; }
    public int PresetXl { get; private set; }
    public bool RequireTaskApproval { get; private set; }

    public string BrandColor { get; private set; } = DefaultBrandColor;
    public Guid? LogoLightFileId { get; private set; }
    public Guid? LogoDarkFileId { get; private set; }
    public int PaymentDeadlineHours { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }
    public Guid? UpdatedById { get; private set; }

    /// <summary>The defaults the organization agreed (also the migration seed).</summary>
    public static OrgSettings Defaults() => new()
    {
        Id = SingletonId,
        LeadMultiplier = 1.0m,
        CollabMultiplier = 0.7m,
        AssistantMultiplier = 0.3m,
        PresetSmall = 10,
        PresetMedium = 20,
        PresetLarge = 40,
        PresetXl = 80,
        RequireTaskApproval = true,
        BrandColor = DefaultBrandColor,
        PaymentDeadlineHours = 48,
    };

    public static bool IsValidMultiplier(decimal value) =>
        value >= 0m && value <= MaxMultiplier && decimal.Round(value, 1) == value;

    public static bool ArePresetsValid(int small, int medium, int large, int xl) =>
        small >= MinPreset && xl <= MaxPreset && small < medium && medium < large && large < xl;

    public void UpdatePoints(
        decimal lead, decimal collab, decimal assistant, int small, int medium, int large, int xl, bool requireTaskApproval,
        DateTimeOffset at, Guid? actorId)
    {
        if (!IsValidMultiplier(lead) || !IsValidMultiplier(collab) || !IsValidMultiplier(assistant))
            throw new DomainException("Multipliers go from 0 to 2 in steps of 0.1.");
        if (!ArePresetsValid(small, medium, large, xl))
            throw new DomainException($"Size presets must be {MinPreset}–{MaxPreset} and increase: small < medium < large < XL.");
        LeadMultiplier = lead;
        CollabMultiplier = collab;
        AssistantMultiplier = assistant;
        PresetSmall = small;
        PresetMedium = medium;
        PresetLarge = large;
        PresetXl = xl;
        RequireTaskApproval = requireTaskApproval;
        Touch(at, actorId);
    }

    /// <summary>Brand color must be <c>#RRGGBB</c> with at least 4.5:1 contrast against white (white text sits on it).</summary>
    public void UpdateBranding(string brandColor, int paymentDeadlineHours, DateTimeOffset at, Guid? actorId)
    {
        if (!ColorContrast.IsHex(brandColor)) throw new DomainException("Brand color must look like #B71C1C.");
        if (!ColorContrast.HasEnoughContrastWithWhite(brandColor))
            throw new DomainException("Brand color needs at least 4.5:1 contrast with white.");
        if (paymentDeadlineHours is < MinPaymentDeadlineHours or > MaxPaymentDeadlineHours)
            throw new DomainException($"Payment deadline must be {MinPaymentDeadlineHours}–{MaxPaymentDeadlineHours} hours.");
        BrandColor = brandColor.ToUpperInvariant();
        PaymentDeadlineHours = paymentDeadlineHours;
        Touch(at, actorId);
    }

    public void SetLogo(LogoVariant variant, Guid fileId, DateTimeOffset at, Guid? actorId)
    {
        switch (variant)
        {
            case LogoVariant.Light: LogoLightFileId = fileId; break;
            case LogoVariant.Dark: LogoDarkFileId = fileId; break;
            default: throw new DomainException("Unknown logo variant.");
        }
        Touch(at, actorId);
    }

    /// <summary>The stored brand color if it is still a safe, valid hex value; otherwise the default.</summary>
    public static string SafeBrandColor(string? stored) =>
        ColorContrast.IsHex(stored) ? stored! : DefaultBrandColor;

    private void Touch(DateTimeOffset at, Guid? actorId)
    {
        UpdatedAt = at;
        UpdatedById = actorId;
    }
}
