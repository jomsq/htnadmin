namespace Htn.Domain.Organization;

public enum PersonLevel
{
    Board,
    Goon,
    Apprentice,
    Member,
}

public static class PersonLevelExtensions
{
    /// <summary>Board, Goon and Apprentice get a login; Members are roster only.</summary>
    public static bool HasLogin(this PersonLevel level) => level is PersonLevel.Board or PersonLevel.Goon or PersonLevel.Apprentice;
}
