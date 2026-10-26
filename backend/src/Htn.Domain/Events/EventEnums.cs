namespace Htn.Domain.Events;

/// <summary>
/// Event lifecycle. <see cref="Draft"/>, <see cref="OnSale"/> and <see cref="Closed"/> are stored;
/// <see cref="Ended"/> is never stored: it is derived when the end date has passed (see <see cref="Event.StatusAt"/>).
/// </summary>
public enum EventStatus
{
    Draft,
    OnSale,
    Closed,
    Ended,
}

/// <summary>How registrants send proof of payment: an upload page, or by replying to the billing email.</summary>
public enum ProofMode
{
    Upload,
    ReplyEmail,
}

public enum TicketVisibility
{
    Public,

    /// <summary>Not on the public page (comps, assigned tickets). Usable in manual registration.</summary>
    Hidden,
}

/// <summary>Derived ticket status shown in the tickets table.</summary>
public enum TicketStatus
{
    OnSale,
    Scheduled,
    Ended,
    SoldOut,
    Hidden,
    Inactive,
}

public enum VoucherKind
{
    Percent,
    Fixed,
}

public enum VoucherSource
{
    Manual,

    /// <summary>Created automatically for a speaker's plus-one (Phase 6).</summary>
    SpeakerPlusOne,
}

/// <summary>Why a voucher can't be applied. Public callers only ever learn "valid" or "not valid".</summary>
public enum VoucherError
{
    None,
    Inactive,
    Expired,
    NotForTicket,
    UsedUp,
}
