using Htn.Infrastructure.Data;

namespace Htn.Api.Features.Events;

/// <summary>
/// The one place that counts what has been sold and used. Everything (lists, details, price lock, quantity floor,
/// voucher limits, and in 3B the capacity check) reads these counts, so registrations only need to be wired in here.
/// <list type="bullet">
/// <item><b>Event sold</b>: attendees of registrations in PendingPayment, Paid or CheckedIn.</item>
/// <item><b>Ticket sold</b>: those registrations of the ticket type (one per checkout; a group of 5 is 1 of the quantity).</item>
/// <item><b>Voucher used</b>: those registrations that applied the voucher.</item>
/// </list>
/// Until registrations exist (slice 3B) every count is 0. Missing keys mean 0: use <see cref="Of"/>.
/// </summary>
public sealed record EventUsageCounts(
    IReadOnlyDictionary<Guid, int> AttendeesByEvent,
    IReadOnlyDictionary<Guid, int> SoldByTicketType,
    IReadOnlyDictionary<Guid, int> UsedByVoucher)
{
    public static readonly EventUsageCounts None = new(
        new Dictionary<Guid, int>(), new Dictionary<Guid, int>(), new Dictionary<Guid, int>());

    public static int Of(IReadOnlyDictionary<Guid, int> counts, Guid id) => counts.TryGetValue(id, out var n) ? n : 0;

    public int EventSold(Guid eventId) => Of(AttendeesByEvent, eventId);
    public int TicketSold(Guid ticketTypeId) => Of(SoldByTicketType, ticketTypeId);
    public int VoucherUsed(Guid voucherId) => Of(UsedByVoucher, voucherId);
}

public static class EventUsage
{
    /// <summary>Counts for the given events (attendees per event, and per ticket type / voucher of those events).</summary>
    public static Task<EventUsageCounts> ForEventsAsync(HtnDbContext db, IReadOnlyCollection<Guid> eventIds, CancellationToken ct)
    {
        // 3B: group non-cancelled registrations (status IN PendingPayment, Paid, CheckedIn) of eventIds by event,
        // ticket type and voucher here. Nothing is registered yet, so all counts are 0.
        _ = db;
        _ = eventIds;
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(EventUsageCounts.None);
    }

    public static Task<EventUsageCounts> ForEventAsync(HtnDbContext db, Guid eventId, CancellationToken ct) =>
        ForEventsAsync(db, [eventId], ct);
}
