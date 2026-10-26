using System.Globalization;
using System.Text.RegularExpressions;

namespace Htn.Domain.Settings;

/// <summary>WCAG 2.x relative luminance and contrast ratio for <c>#RRGGBB</c> colors.</summary>
public static partial class ColorContrast
{
    /// <summary>WCAG AA for normal text.</summary>
    public const double MinimumAgainstWhite = 4.5;

    // \z, not $: "$" also matches before a trailing newline, so "#B71C1C\n" would pass.
    [GeneratedRegex(@"^#[0-9A-Fa-f]{6}\z")]
    private static partial Regex HexPattern();

    public static bool IsHex(string? value) => value is not null && HexPattern().IsMatch(value);

    public static double RatioAgainstWhite(string hex) => Ratio(hex, "#FFFFFF");

    public static double Ratio(string a, string b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        var (hi, lo) = la > lb ? (la, lb) : (lb, la);
        return (hi + 0.05) / (lo + 0.05);
    }

    public static double Luminance(string hex)
    {
        if (!IsHex(hex)) throw new FormatException("Expected a #RRGGBB color.");
        double Channel(int offset)
        {
            var c = int.Parse(hex.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
    }

    public static bool HasEnoughContrastWithWhite(string hex) => IsHex(hex) && RatioAgainstWhite(hex) >= MinimumAgainstWhite;
}
