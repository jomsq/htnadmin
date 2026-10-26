using Htn.Domain.Common;
using Htn.Domain.Files;
using Htn.Domain.Settings;

namespace Htn.Domain.Tests;

public sealed class SettingsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("#B71C1C", true)]   // brand default, ~6.5:1
    [InlineData("#000000", true)]
    [InlineData("#767676", true)]   // ~4.54:1, the lightest gray that passes
    [InlineData("#777777", false)]  // ~4.48:1
    [InlineData("#FFEB3B", false)]  // yellow
    [InlineData("#FFFFFF", false)]
    public void Contrast_against_white(string color, bool ok) => Assert.Equal(ok, ColorContrast.HasEnoughContrastWithWhite(color));

    [Fact]
    public void Contrast_ratio_matches_wcag()
    {
        Assert.Equal(21.0, ColorContrast.Ratio("#000000", "#FFFFFF"), 2);
        Assert.Equal(1.0, ColorContrast.Ratio("#FFFFFF", "#ffffff"), 2);
    }

    [Theory]
    [InlineData("B71C1C")]
    [InlineData("#B71C1")]
    [InlineData("#B71C1CZ")]
    [InlineData("red")]
    [InlineData("#B71C1C;background:url(x)")]
    public void Brand_color_must_be_hex(string color)
    {
        var s = OrgSettings.Defaults();
        Assert.Throws<DomainException>(() => s.UpdateBranding(color, 48, Now, null));
        Assert.Equal(OrgSettings.DefaultBrandColor, OrgSettings.SafeBrandColor(color));
    }

    [Fact]
    public void Branding_rejects_low_contrast_and_bad_deadline()
    {
        var s = OrgSettings.Defaults();
        Assert.Throws<DomainException>(() => s.UpdateBranding("#FFEB3B", 48, Now, null));
        Assert.Throws<DomainException>(() => s.UpdateBranding("#0D47A1", 0, Now, null));
        Assert.Throws<DomainException>(() => s.UpdateBranding("#0D47A1", 337, Now, null));
        s.UpdateBranding("#0d47a1", 72, Now, null);
        Assert.Equal("#0D47A1", s.BrandColor);
        Assert.Equal(72, s.PaymentDeadlineHours);
    }

    [Theory]
    [InlineData(0.0, true)]
    [InlineData(2.0, true)]
    [InlineData(0.7, true)]
    [InlineData(0.75, false)]
    [InlineData(2.1, false)]
    [InlineData(-0.1, false)]
    public void Multipliers_are_0_to_2_in_tenths(decimal value, bool ok) => Assert.Equal(ok, OrgSettings.IsValidMultiplier(value));

    [Fact]
    public void Presets_must_increase_within_range()
    {
        Assert.True(OrgSettings.ArePresetsValid(10, 20, 40, 80));
        Assert.False(OrgSettings.ArePresetsValid(10, 10, 40, 80));
        Assert.False(OrgSettings.ArePresetsValid(0, 20, 40, 80));
        Assert.False(OrgSettings.ArePresetsValid(10, 20, 40, 1001));
        var s = OrgSettings.Defaults();
        Assert.Throws<DomainException>(() => s.UpdatePoints(1, 0.7m, 0.3m, 40, 20, 10, 80, true, Now, null));
        s.UpdatePoints(1.5m, 0.5m, 0.2m, 5, 15, 30, 60, false, Now, null);
        Assert.Equal(1.5m, s.LeadMultiplier);
        Assert.False(s.RequireTaskApproval);
    }

    [Theory]
    [InlineData("0917 123 4567", "•••• 4567")]
    [InlineData("1234-5678-90", "•••• 7890")]
    [InlineData("123", "••••")]
    [InlineData(null, "••••")]
    public void Account_numbers_are_masked(string? number, string masked) => Assert.Equal(masked, PaymentAccount.Mask(number));

    [Fact]
    public void Payment_account_rules()
    {
        Assert.Throws<DomainException>(() => PaymentAccount.Create(PaymentAccountKind.Bank, "BPI", null, "HTN Inc.", "1234", Now));
        Assert.Throws<DomainException>(() => PaymentAccount.Create(PaymentAccountKind.GCash, "GCash", null, "HTN Inc.", null, Now));
        var cash = PaymentAccount.Create(PaymentAccountKind.Cash, "Cash at desk", "ignored", "HTN Inc.", null, Now);
        Assert.Null(cash.BankName);

        var gcash = PaymentAccount.Create(PaymentAccountKind.GCash, "GCash — HTN", null, "HackTheNorth.PH Inc.", "0917 123 4567", Now);
        gcash.SetDefault(true);
        Assert.Throws<DomainException>(() => gcash.Deactivate(" ", Now));
        gcash.Deactivate("Closed", Now);
        Assert.False(gcash.IsActive);
        Assert.False(gcash.IsDefault);
        Assert.Throws<DomainException>(() => gcash.SetDefault(true));
        Assert.Throws<DomainException>(() => gcash.Update(PaymentAccountKind.GCash, "x", null, "y", "1234"));
    }

    [Fact]
    public void Png_signature_is_checked_from_bytes()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R', .. new byte[17]];
        Assert.True(FileSignatures.IsPng(png));
        Assert.False(FileSignatures.IsPng("<svg onload=alert(1)>"u8.ToArray()));
        Assert.False(FileSignatures.IsPng(png[..20]));
        var gif = (byte[])png.Clone();
        gif[1] = (byte)'G';
        Assert.False(FileSignatures.IsPng(gif));
    }

    [Theory]
    [InlineData("#B71C1C\n")]
    [InlineData("#B71C1C\r\n")]
    [InlineData("\n#B71C1C")]
    public void Hex_check_rejects_surrounding_newlines(string value) => Assert.False(ColorContrast.IsHex(value));

    [Fact]
    public void Png_size_is_read_from_ihdr()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R',
            0, 0, 0x10, 0x00, 0, 0, 0x01, 0x2C, .. new byte[9]];
        Assert.True(FileSignatures.IsPng(png));
        Assert.Equal((4096u, 300u), FileSignatures.PngSize(png));
    }
}
