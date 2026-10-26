using Htn.Domain.Common;

namespace Htn.Domain.Tests;

/// <summary>
/// The quarter window behind <c>GET /api/leaderboard?scope=quarter</c>. The organization's calendar is Asia/Manila
/// (UTC+8, no DST), so a quarter runs from the 1st at 00:00 Manila to the same instant three months later, exclusive.
/// </summary>
public sealed class ManilaTimeTests
{
    [Fact]
    public void Zone_is_utc_plus_8_with_or_without_tzdata()
    {
        // Either the real Asia/Manila or the fixed-offset fallback: both are +08:00 all year, which is why the
        // fallback is exact rather than an approximation.
        Assert.Equal(TimeSpan.FromHours(8), ManilaTime.Zone.GetUtcOffset(new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Unspecified)));
        Assert.Equal(TimeSpan.FromHours(8), ManilaTime.Zone.GetUtcOffset(new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Unspecified)));
        Assert.False(ManilaTime.Zone.SupportsDaylightSavingTime && ManilaTime.Zone.GetAdjustmentRules().Any(r => r.DateEnd.Year >= 2026));
    }

    [Theory]
    [InlineData("2026-01-01T00:00:00+08:00", 2026, 1)]
    [InlineData("2026-03-31T23:59:59+08:00", 2026, 1)]
    [InlineData("2026-04-01T00:00:00+08:00", 2026, 2)]
    [InlineData("2026-06-30T16:00:00+08:00", 2026, 2)]
    [InlineData("2026-07-01T00:00:00+08:00", 2026, 3)]
    [InlineData("2026-10-26T09:30:00+08:00", 2026, 4)]
    [InlineData("2026-12-31T23:59:59+08:00", 2026, 4)]
    public void Quarter_follows_the_manila_calendar(string at, int year, int quarter)
    {
        var window = ManilaTime.Quarter(DateTimeOffset.Parse(at));
        Assert.Equal(year, window.Year);
        Assert.Equal(quarter, window.Quarter);
        Assert.Equal($"Q{quarter} {year}", window.Label);
    }

    [Fact]
    public void Quarter_starts_at_midnight_manila_and_is_half_open()
    {
        var window = ManilaTime.Quarter(DateTimeOffset.Parse("2026-11-05T03:00:00Z"));
        // Same instants, expressed in UTC so they can go straight into a timestamptz parameter.
        Assert.Equal(DateTimeOffset.Parse("2026-10-01T00:00:00+08:00"), window.Start);
        Assert.Equal(DateTimeOffset.Parse("2027-01-01T00:00:00+08:00"), window.End);
        Assert.Equal(TimeSpan.Zero, window.Start.Offset);
        Assert.Equal(TimeSpan.Zero, window.End.Offset);
        // 15:59 UTC on Sep 30 is still Sep 30 in Manila; one minute later it is Oct 1, 00:00.
        Assert.False(window.Contains(DateTimeOffset.Parse("2026-09-30T15:59:59Z")));
        Assert.True(window.Contains(window.Start));
        Assert.True(window.Contains(DateTimeOffset.Parse("2026-09-30T16:00:00Z")));
        Assert.True(window.Contains(window.End.AddTicks(-1)));
        Assert.False(window.Contains(window.End));
    }

    [Fact]
    public void Quarter_is_computed_in_manila_not_utc()
    {
        // 2026-03-31 17:00 UTC is already 2026-04-01 01:00 in Manila: Q2, not Q1.
        var window = ManilaTime.Quarter(DateTimeOffset.Parse("2026-03-31T17:00:00Z"));
        Assert.Equal(2, window.Quarter);
        Assert.Equal(DateTimeOffset.Parse("2026-04-01T00:00:00+08:00"), window.Start);
    }

    [Fact]
    public void Quarter_crosses_the_year_boundary()
    {
        var window = ManilaTime.Quarter(DateTimeOffset.Parse("2025-12-31T20:00:00+08:00"));
        Assert.Equal(2025, window.Year);
        Assert.Equal(4, window.Quarter);
        Assert.Equal(DateTimeOffset.Parse("2026-01-01T00:00:00+08:00"), window.End);
        Assert.Equal(2026, ManilaTime.Quarter(window.End).Year);
        Assert.Equal(1, ManilaTime.Quarter(window.End).Quarter);
    }
}
