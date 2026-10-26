namespace Htn.Domain.Organization;

public enum PositionKind
{
    /// <summary>A Board of Trustees seat (Chairperson, Vice Chairperson, Treasurer, Corporate Secretary, Trustee). Held by Board.</summary>
    BoardSeat,

    /// <summary>An organizer position (e.g. Head of Logistics). Held by a Goon.</summary>
    GoonPosition,
}
