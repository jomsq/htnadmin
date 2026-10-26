using Htn.Api.Auth;
using Htn.Api.Features.Settings;
using Htn.Domain.Events;
using Htn.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Htn.Api.Features.Events;

/// <summary>DTO projections and lookups shared by the event, ticket and voucher endpoints.</summary>
internal static class EventViews
{
    /// <summary>Events.Manage holders see Drafts and vouchers; everyone else with Events.View does not.</summary>
    public static async Task<bool> CanManageAsync(HttpContext http, IAuthorizationService auth) =>
        (await auth.AuthorizeAsync(http.User, Policies.EventsManage)).Succeeded;

    public static string? CoverUrl(Event e) => e.CoverFileId is { } id ? FilesEndpoints.PublicFileUrl(id) : null;

    public static EventSummaryDto Summary(Event e, EventUsageCounts usage, DateTimeOffset now) => new(
        e.Id, e.Slug, e.Name, e.StatusAt(now), e.StartsAt, e.EndsAt, e.Venue, e.Capacity, usage.EventSold(e.Id), CoverUrl(e), e.IsMainEvent);

    public static TicketTypeDto Ticket(TicketType t, EventUsageCounts usage, DateTimeOffset eventEndsAt, DateTimeOffset now)
    {
        var sold = usage.TicketSold(t.Id);
        return new TicketTypeDto(t.Id, t.Name, t.Price, t.Quantity, sold, t.GroupSize, t.SalesStartsAt, t.SalesEndsAt,
            t.Visibility, t.IsActive, t.SortOrder, t.StatusAt(now, sold, eventEndsAt));
    }

    public static VoucherDto Voucher(Voucher v, EventUsageCounts usage) => new(
        v.Id, v.Code, v.Kind, v.Value, v.TicketTypeIds, v.UsageLimit, usage.VoucherUsed(v.Id), v.ExpiresAt, v.IsActive, v.Source);

    public static async Task<EventDetailDto> DetailAsync(HtnDbContext db, Event e, bool manager, DateTimeOffset now, CancellationToken ct)
    {
        var usage = await EventUsage.ForEventAsync(db, e.Id, ct);
        var tickets = await db.TicketTypes.AsNoTracking().Where(t => t.EventId == e.Id)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Id).ToListAsync(ct);
        var vouchers = manager
            ? await db.Vouchers.AsNoTracking().Where(v => v.EventId == e.Id).OrderBy(v => v.CreatedAt).ThenBy(v => v.Id).ToListAsync(ct)
            : [];
        return new EventDetailDto(
            e.Id, e.Slug, e.Name, e.StatusAt(now), e.StartsAt, e.EndsAt, e.Venue, e.Capacity, usage.EventSold(e.Id), CoverUrl(e), e.IsMainEvent,
            e.Description, e.ManualRegistration, e.GroupRegistration, e.PublicPage, e.ProofMode, e.MembershipFee, e.MembershipYear,
            tickets.Select(t => Ticket(t, usage, e.EndsAt, now)).ToList(),
            vouchers.Select(v => Voucher(v, usage)).ToList());
    }

    /// <summary>What the audit log keeps about an event (no derived values).</summary>
    public static object Audit(Event e) => new
    {
        e.Slug, e.Name, e.StartsAt, e.EndsAt, e.Venue, e.Capacity, e.Description, e.Status, e.CoverFileId,
        e.ManualRegistration, e.GroupRegistration, e.PublicPage, e.ProofMode, e.IsMainEvent, e.MembershipFee, e.MembershipYear,
    };

    public static object Audit(TicketType t) => new
    {
        t.EventId, t.Name, t.Price, t.Quantity, t.GroupSize, t.SalesStartsAt, t.SalesEndsAt, t.Visibility, t.IsActive, t.SortOrder,
    };

    public static object Audit(Voucher v) => new
    {
        v.EventId, v.Code, v.Kind, v.Value, v.TicketTypeIds, v.UsageLimit, v.ExpiresAt, v.IsActive, v.Source,
    };

    public static bool IsUniqueViolation(DbUpdateException ex, string constraint) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && string.Equals(pg.ConstraintName, constraint, StringComparison.Ordinal);
}
