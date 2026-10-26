namespace Htn.Domain.Organization;

public enum LevelChangeKind
{
    /// <summary>Already at that level.</summary>
    Same,

    /// <summary>One step up the path (Member → Apprentice → Goon) or any level → Board.</summary>
    Promotion,

    /// <summary>Any move down. Needs a reason.</summary>
    Demotion,

    /// <summary>An upward move that skips a step (e.g. Member → Goon).</summary>
    SkipsAStep,
}

/// <summary>The level path: Member → Apprentice → Goon, with Board as a separate seat reachable from any level.</summary>
public static class PromotionRules
{
    /// <summary>Higher is more senior. Board sits above Goon.</summary>
    public static int Rank(PersonLevel level) => level switch
    {
        PersonLevel.Member => 0,
        PersonLevel.Apprentice => 1,
        PersonLevel.Goon => 2,
        PersonLevel.Board => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };

    public static LevelChangeKind Classify(PersonLevel from, PersonLevel to)
    {
        if (from == to) return LevelChangeKind.Same;
        if (to == PersonLevel.Board) return LevelChangeKind.Promotion;
        var diff = Rank(to) - Rank(from);
        if (diff < 0) return LevelChangeKind.Demotion;
        return diff == 1 ? LevelChangeKind.Promotion : LevelChangeKind.SkipsAStep;
    }

    /// <summary>The next step up for the person drawer's Promote button; null for Goon (Board is a seat, not a step) and Board.</summary>
    public static PersonLevel? NextStep(PersonLevel level) => level switch
    {
        PersonLevel.Member => PersonLevel.Apprentice,
        PersonLevel.Apprentice => PersonLevel.Goon,
        _ => null,
    };

    /// <summary>The level a holder of a position of this kind must be at.</summary>
    public static PersonLevel RequiredLevel(PositionKind kind) => kind switch
    {
        PositionKind.BoardSeat => PersonLevel.Board,
        PositionKind.GoonPosition => PersonLevel.Goon,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
