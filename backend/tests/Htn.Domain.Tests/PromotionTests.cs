using Htn.Domain.Common;
using Htn.Domain.Organization;

namespace Htn.Domain.Tests;

public sealed class PromotionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 8, 0, 0, TimeSpan.Zero);

    private static Person New(PersonLevel level) =>
        Person.Create("Paolo", null, "Garcia", $"{Guid.NewGuid():N}@hackthenorth.ph", level, null, Now, null);

    [Theory]
    [InlineData(PersonLevel.Member, PersonLevel.Apprentice, LevelChangeKind.Promotion)]
    [InlineData(PersonLevel.Apprentice, PersonLevel.Goon, LevelChangeKind.Promotion)]
    [InlineData(PersonLevel.Member, PersonLevel.Board, LevelChangeKind.Promotion)]
    [InlineData(PersonLevel.Apprentice, PersonLevel.Board, LevelChangeKind.Promotion)]
    [InlineData(PersonLevel.Goon, PersonLevel.Board, LevelChangeKind.Promotion)]
    [InlineData(PersonLevel.Member, PersonLevel.Goon, LevelChangeKind.SkipsAStep)]
    [InlineData(PersonLevel.Goon, PersonLevel.Apprentice, LevelChangeKind.Demotion)]
    [InlineData(PersonLevel.Goon, PersonLevel.Member, LevelChangeKind.Demotion)]
    [InlineData(PersonLevel.Board, PersonLevel.Goon, LevelChangeKind.Demotion)]
    [InlineData(PersonLevel.Board, PersonLevel.Member, LevelChangeKind.Demotion)]
    [InlineData(PersonLevel.Goon, PersonLevel.Goon, LevelChangeKind.Same)]
    public void Classify_follows_the_level_path(PersonLevel from, PersonLevel to, LevelChangeKind expected) =>
        Assert.Equal(expected, PromotionRules.Classify(from, to));

    [Fact]
    public void Promote_one_step_records_history()
    {
        var p = New(PersonLevel.Member);
        Assert.Equal(LevelChangeKind.Promotion, p.Promote(PersonLevel.Apprentice, Now, null, null));
        Assert.Equal(PersonLevel.Apprentice, p.Level);
        var h = p.History[^1];
        Assert.Equal(PersonHistoryKinds.LevelChanged, h.Kind);
        Assert.Equal("Member", h.FromValue);
        Assert.Equal("Apprentice", h.ToValue);
    }

    [Fact]
    public void Promote_rejects_skipping_a_step()
    {
        var p = New(PersonLevel.Member);
        Assert.Equal(PromotionError.SkipsAStep, p.CheckPromotion(PersonLevel.Goon, "fast track"));
        Assert.Throws<DomainException>(() => p.Promote(PersonLevel.Goon, Now, null, "fast track"));
        Assert.Equal(PersonLevel.Member, p.Level);
    }

    [Fact]
    public void Demotion_needs_a_reason()
    {
        var p = New(PersonLevel.Goon);
        Assert.Equal(PromotionError.ReasonRequired, p.CheckPromotion(PersonLevel.Apprentice, "  "));
        Assert.Throws<DomainException>(() => p.Promote(PersonLevel.Apprentice, Now, null, null));

        Assert.Equal(LevelChangeKind.Demotion, p.Promote(PersonLevel.Apprentice, Now, null, "  Inactive for a year "));
        Assert.Equal("Inactive for a year", p.History[^1].Reason);
    }

    [Fact]
    public void Same_level_and_deactivated_are_refused()
    {
        var p = New(PersonLevel.Goon);
        Assert.Equal(PromotionError.SameLevel, p.CheckPromotion(PersonLevel.Goon, null));
        p.Deactivate(Now, null, null);
        Assert.Equal(PromotionError.Inactive, p.CheckPromotion(PersonLevel.Board, null));
    }

    [Theory]
    [InlineData(PersonLevel.Member, PersonLevel.Apprentice)]
    [InlineData(PersonLevel.Apprentice, PersonLevel.Goon)]
    public void Next_step_is_one_up(PersonLevel level, PersonLevel next) => Assert.Equal(next, PromotionRules.NextStep(level));

    [Fact]
    public void Rename_and_change_email_normalize()
    {
        var p = New(PersonLevel.Member);
        Assert.True(p.Rename(" Juan ", " ", " Dela Cruz "));
        Assert.Equal("Juan", p.FirstName);
        Assert.Null(p.MiddleName);
        Assert.False(p.Rename("Juan", null, "Dela Cruz"));
        Assert.True(p.ChangeEmail(" Juan@Example.PH "));
        Assert.Equal("juan@example.ph", p.Email);
    }
}
