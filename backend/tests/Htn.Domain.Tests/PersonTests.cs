using Htn.Domain.Common;
using Htn.Domain.Organization;

namespace Htn.Domain.Tests;

public sealed class PersonTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 8, 0, 0, TimeSpan.Zero);

    private static Person NewGoon() =>
        Person.Create(" Mika ", null, "Soriano", " Mika@HackTheNorth.PH ", PersonLevel.Goon, "Head of Logistics", Now, Guid.NewGuid());

    [Fact]
    public void Create_normalizes_and_records_history()
    {
        var p = NewGoon();
        Assert.Equal("Mika", p.FirstName);
        Assert.Equal("mika@hackthenorth.ph", p.Email);
        Assert.True(p.IsActive);
        Assert.Equal(7, p.Id.Version);
        var h = Assert.Single(p.History);
        Assert.Equal(PersonHistoryKinds.Created, h.Kind);
    }

    [Fact]
    public void Create_rejects_blank_names()
    {
        Assert.Throws<DomainException>(() => Person.Create(" ", null, "X", "a@b.ph", PersonLevel.Member, null, Now, null));
    }

    [Fact]
    public void ChangeLevel_keeps_history()
    {
        var p = NewGoon();
        p.ChangeLevel(PersonLevel.Board, Now.AddDays(1), null, "Elected");
        Assert.Equal(PersonLevel.Board, p.Level);
        var h = p.History[^1];
        Assert.Equal(PersonHistoryKinds.LevelChanged, h.Kind);
        Assert.Equal("Goon", h.FromValue);
        Assert.Equal("Board", h.ToValue);
        Assert.Throws<DomainException>(() => p.ChangeLevel(PersonLevel.Board, Now, null, null));
    }

    [Fact]
    public void Deactivate_once_and_blocks_changes()
    {
        var p = NewGoon();
        p.Deactivate(Now, null, null);
        Assert.False(p.IsActive);
        Assert.Equal(Now, p.DeactivatedAt);
        Assert.Throws<DomainException>(() => p.Deactivate(Now, null, null));
        Assert.Throws<DomainException>(() => p.ChangeLevel(PersonLevel.Apprentice, Now, null, null));
    }

    [Theory]
    [InlineData(PersonLevel.Board, true)]
    [InlineData(PersonLevel.Goon, true)]
    [InlineData(PersonLevel.Apprentice, true)]
    [InlineData(PersonLevel.Member, false)]
    public void Only_board_goon_apprentice_have_login(PersonLevel level, bool expected) =>
        Assert.Equal(expected, level.HasLogin());

    [Fact]
    public void Deactivate_records_trimmed_reason_and_limits_length()
    {
        var p = NewGoon();
        Assert.Throws<DomainException>(() => p.Deactivate(Now, null, new string('x', Person.ReasonMaxLength + 1)));
        Assert.True(p.IsActive);

        p.Deactivate(Now, null, "  Left the org  ");
        Assert.Equal("Left the org", p.History[^1].Reason);
    }
}
