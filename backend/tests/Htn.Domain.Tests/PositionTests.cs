using Htn.Domain.Common;
using Htn.Domain.Organization;

namespace Htn.Domain.Tests;

public sealed class PositionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 8, 0, 0, TimeSpan.Zero);

    private static Person New(PersonLevel level) =>
        Person.Create("Mika", null, "Soriano", $"{Guid.NewGuid():N}@hackthenorth.ph", level, null, Now, null);

    [Fact]
    public void Assign_sets_holder_history_and_person_position()
    {
        var goon = New(PersonLevel.Goon);
        var pos = Position.Create(PositionKind.GoonPosition, " Head of Logistics ", 1, Now);
        var term = pos.Assign(goon, holdsAnotherBoardSeat: false, Now, null);

        Assert.Equal(goon.Id, pos.HolderId);
        Assert.Equal(Now, pos.HolderSince);
        Assert.Equal("Head of Logistics", goon.Position);
        Assert.Equal(PersonHistoryKinds.PositionAssigned, goon.History[^1].Kind);
        Assert.Equal("Head of Logistics", goon.History[^1].ToValue);
        Assert.Equal(pos.Id, term.PositionId);
        Assert.Null(term.EndedAt);
    }

    [Fact]
    public void Vacate_closes_the_term_and_writes_history()
    {
        var goon = New(PersonLevel.Goon);
        var pos = Position.Create(PositionKind.GoonPosition, "Head of Tech", 1, Now);
        var term = pos.Assign(goon, false, Now, null);
        pos.Vacate(goon, term, Now.AddDays(1), null, " Stepped down ");

        Assert.Null(pos.HolderId);
        Assert.Null(goon.Position);
        Assert.Equal(Now.AddDays(1), term.EndedAt);
        Assert.Equal("Stepped down", term.EndReason);
        Assert.Equal(PersonHistoryKinds.PositionVacated, goon.History[^1].Kind);
        Assert.Throws<DomainException>(() => pos.Vacate(goon, term, Now, null, null));
    }

    [Fact]
    public void Holder_must_be_at_the_right_level()
    {
        var seat = Position.Create(PositionKind.BoardSeat, "Chairperson", 1, Now);
        var goonPos = Position.Create(PositionKind.GoonPosition, "Head of Programs", 1, Now);
        Assert.Equal(AssignError.WrongLevel, seat.CheckAssign(New(PersonLevel.Goon), false));
        Assert.Equal(AssignError.WrongLevel, goonPos.CheckAssign(New(PersonLevel.Board), false));
        Assert.Equal(AssignError.WrongLevel, goonPos.CheckAssign(New(PersonLevel.Apprentice), false));
        Assert.Throws<DomainException>(() => seat.Assign(New(PersonLevel.Member), false, Now, null));
    }

    [Fact]
    public void One_board_seat_per_person_and_no_double_assign()
    {
        var board = New(PersonLevel.Board);
        var seat = Position.Create(PositionKind.BoardSeat, "Trustee", 5, Now);
        Assert.Equal(AssignError.AlreadyHoldsBoardSeat, seat.CheckAssign(board, holdsAnotherBoardSeat: true));
        seat.Assign(board, false, Now, null);
        Assert.Equal(AssignError.AlreadyHolder, seat.CheckAssign(board, false));
        Assert.Throws<DomainException>(() => seat.Assign(New(PersonLevel.Board), false, Now, null)); // held: vacate first
    }

    [Fact]
    public void Inactive_position_or_person_cannot_be_assigned()
    {
        var pos = Position.Create(PositionKind.GoonPosition, "Head of Marketing", 1, Now);
        var goon = New(PersonLevel.Goon);
        goon.Deactivate(Now, null, null);
        Assert.Equal(AssignError.PersonInactive, pos.CheckAssign(goon, false));
        pos.SetActive(false);
        Assert.Equal(AssignError.PositionInactive, pos.CheckAssign(New(PersonLevel.Goon), false));
    }

    [Fact]
    public void Held_position_cannot_be_deactivated()
    {
        var pos = Position.Create(PositionKind.GoonPosition, "Head of Partnerships", 1, Now);
        pos.Assign(New(PersonLevel.Goon), false, Now, null);
        Assert.Throws<DomainException>(() => pos.SetActive(false));
    }
}
