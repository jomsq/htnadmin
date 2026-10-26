using Htn.Domain.Registrations;
using Htn.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Events;

/// <summary>
/// The one place that counts what has been sold and used. Everything (lists, details, price lock, quantity floor,
/// voucher limits and the checkout's capacity check) reads these counts.
/// <list type="bullet">
/// <item><b>Event sold</b>: attendees of registrations that hold a seat: Paid, CheckedIn, or PendingPayment before the
/// deadline. Overdue registrations stay PendingPayment but release their seat (marking one paid re-checks capacity).</item>
/// <item><b>Ticket sold</b>: those registrations of the ticket type (one per checkout; a group of 5 is 1 of the quantity).</item>
/// <item><b>Voucher used</b>: those registrations that applied the voucher.</item>
/// </list>
/// Missing keys mean 0: use <see cref="Of"/>. Inside a checkout the counts are read after the event row is locked
/// (see <c>Checkout</c>), so they can't change underneath it.
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
    public static async Task<EventUsageCounts> ForEventsAsync(
        HtnDbContext db, IReadOnlyCollection<Guid> eventIds, DateTimeOffset now, CancellationToken ct)
    {
        if (eventIds.Count == 0) return EventUsageCounts.None;
        var ids = eventIds.Distinct().ToList();
        // One grouped, index-only scan of the events' seat-holding registrations (ix_registrations_usage).
        var rows = await db.Registrations.AsNoTracking()
            .Where(r => ids.Contains(r.EventId) && r.Status != RegistrationStatus.Cancelled
                        && (r.Status != RegistrationStatus.PendingPayment || r.Deadline == null || r.Deadline >= now))
            .GroupBy(r => new { r.EventId, r.TicketTypeId, r.VoucherId })
            .Select(g => new { g.Key.EventId, g.Key.TicketTypeId, g.Key.VoucherId, Checkouts = g.Count(), Seats = g.Sum(r => r.Seats) })
            .ToListAsync(ct);

        var byEvent = rows.GroupBy(r => r.EventId).ToDictionary(g => g.Key, g => g.Sum(r => r.Seats));
        var byTicket = rows.GroupBy(r => r.TicketTypeId).ToDictionary(g => g.Key, g => g.Sum(r => r.Checkouts));
        var byVoucher = rows.Where(r => r.VoucherId is not null).GroupBy(r => r.VoucherId!.Value).ToDictionary(g => g.Key, g => g.Sum(r => r.Checkouts));
        return new EventUsageCounts(byEvent, byTicket, byVoucher);
    }

    public static Task<EventUsageCounts> ForEventAsync(HtnDbContext db, Guid eventId, DateTimeOffset now, CancellationToken ct) =>
        ForEventsAsync(db, [eventId], now, ct);
}

/// <summary>
/// The per-event write lock. Every write whose correctness depends on <see cref="EventUsage"/> (checkout, mark paid,
/// cancel, revert, capacity / ticket / voucher changes) takes it first, inside its transaction, and only then reads the
/// counts. Lock order rule: the event row first, always, then registrations; never the other way round.
/// <c>FOR NO KEY UPDATE</c> serializes these writers without blocking inserts that reference the event (foreign-key
/// checks take <c>FOR KEY SHARE</c>).
/// </summary>
public static class EventLock
{
    /// <summary>Locks the event row until the current transaction ends. False when the event doesn't exist.</summary>
    public static async Task<bool> AcquireAsync(HtnDbContext db, Guid eventId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("EventLock needs a transaction.");
        var rows = await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM events WHERE id = {eventId} FOR NO KEY UPDATE").ToListAsync(ct);
        return rows.Count == 1;
    }
}
