namespace Htn.Infrastructure.Identity;

public enum ThemeMode { Light, System, Dark }

public enum TextSize { Default, Large }

public enum Density { Comfortable, Compact }

/// <summary>Personal appearance settings (Settings.Personal). Serialized to the client in lower case.</summary>
public sealed class UserPreferences
{
    public ThemeMode Mode { get; set; } = ThemeMode.System;
    public TextSize TextSize { get; set; } = TextSize.Default;
    public Density Density { get; set; } = Density.Comfortable;
}
