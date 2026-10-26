using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Domain.Events;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Events;

public static class VouchersEndpoints
{
    /// <summary>Unique (event_id, code); codes are stored upper-case.</summary>
    public const string CodeIndex = "ux_vouchers_event_code";

    public static RouteGroupBuilder MapVouchers(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/events/{eventId:guid}/vouchers").WithTags("Events");
        g.MapPost("/", Create).RequireAuthorization(Policies.EventsManage).Validate<VoucherRequest>();
        g.MapPut("/{voucherId:guid}", Update).RequireAuthorization(Policies.EventsManage).Validate<VoucherRequest>();
        g.MapPost("/{voucherId:guid}/deactivate", Deactivate).RequireAuthorization(Policies.EventsManage);
        return g;
    }

    private static async Task<IResult> Create(
        Guid eventId, VoucherRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, ct)) return TypedResults.NotFound();
        var scope = req.TicketTypeIds?.Distinct().ToList() ?? [];
        if (await CheckScopeAsync(db, eventId, scope, ct) is { } scopeError) return scopeError;

        var code = Voucher.NormalizeCode(req.Code);
        if (await db.Vouchers.AnyAsync(v => v.EventId == eventId && v.Code == code, ct)) return DuplicateCode();

        var voucher = Voucher.Create(eventId, code, req.Kind!.Value, req.Value!.Value, scope, req.UsageLimit, req.ExpiresAt,
            VoucherSource.Manual, clock.GetUtcNow());
        db.Vouchers.Add(voucher);
        audit.Record(me.Id, "voucher.create", nameof(Voucher), voucher.Id.ToString(), after: EventViews.Audit(voucher));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (EventViews.IsUniqueViolation(ex, CodeIndex))
        {
            return DuplicateCode();
        }
        return TypedResults.Created($"/api/events/{eventId}/vouchers/{voucher.Id}", EventViews.Voucher(voucher, EventUsageCounts.None));
    }

    private static async Task<IResult> Update(
        Guid eventId, Guid voucherId, VoucherRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock,
        CancellationToken ct)
    {
        // The usage-limit floor reads EventUsage, so it runs under the event lock (see EventLock).
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await EventLock.AcquireAsync(db, eventId, ct)) return TypedResults.NotFound();
        var voucher = await db.Vouchers.SingleOrDefaultAsync(v => v.Id == voucherId && v.EventId == eventId, ct);
        if (voucher is null) return TypedResults.NotFound();
        if (!voucher.IsActive) return Inactive();
        var scope = req.TicketTypeIds?.Distinct().ToList() ?? [];
        if (await CheckScopeAsync(db, eventId, scope, ct) is { } scopeError) return scopeError;

        var code = Voucher.NormalizeCode(req.Code);
        if (await db.Vouchers.AnyAsync(v => v.EventId == eventId && v.Code == code && v.Id != voucherId, ct)) return DuplicateCode();

        var usage = await EventUsage.ForEventAsync(db, eventId, clock.GetUtcNow(), ct);
        var used = usage.VoucherUsed(voucher.Id);
        if (req.UsageLimit is { } limit && limit < used)
            return Problems.Field("usageLimit", $"The limit can't be below the {used} already used.", "usage_limit_below_used");

        var before = EventViews.Audit(voucher);
        voucher.Update(code, req.Kind!.Value, req.Value!.Value, scope, req.UsageLimit, req.ExpiresAt, used);
        audit.Record(me.Id, "voucher.update", nameof(Voucher), voucher.Id.ToString(), before, EventViews.Audit(voucher));
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (EventViews.IsUniqueViolation(ex, CodeIndex))
        {
            return DuplicateCode();
        }
        return TypedResults.Ok(EventViews.Voucher(voucher, usage));
    }

    private static async Task<IResult> Deactivate(
        Guid eventId, Guid voucherId, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await EventLock.AcquireAsync(db, eventId, ct)) return TypedResults.NotFound();
        var voucher = await db.Vouchers.SingleOrDefaultAsync(v => v.Id == voucherId && v.EventId == eventId, ct);
        if (voucher is null) return TypedResults.NotFound();
        if (!voucher.IsActive) return Inactive();

        var now = clock.GetUtcNow();
        var before = EventViews.Audit(voucher);
        voucher.Deactivate(now);
        audit.Record(me.Id, "voucher.deactivate", nameof(Voucher), voucher.Id.ToString(), before, EventViews.Audit(voucher));
        await db.SaveChangesAsync(ct);
        var usage = await EventUsage.ForEventAsync(db, eventId, now, ct);
        await tx.CommitAsync(ct);
        return TypedResults.Ok(EventViews.Voucher(voucher, usage));
    }

    /// <summary>Every scoped ticket type must belong to this event.</summary>
    private static async Task<IResult?> CheckScopeAsync(HtnDbContext db, Guid eventId, List<Guid> scope, CancellationToken ct)
    {
        if (scope.Count == 0) return null;
        var found = await db.TicketTypes.CountAsync(t => t.EventId == eventId && scope.Contains(t.Id), ct);
        return found == scope.Count
            ? null
            : Problems.Field("ticketTypeIds", "Choose ticket types of this event.", "unknown_ticket_type");
    }

    private static IResult DuplicateCode() =>
        Problems.Result(StatusCodes.Status409Conflict, "This event already has a voucher with that code.", code: "duplicate_code");

    private static IResult Inactive() =>
        Problems.Result(StatusCodes.Status409Conflict, "This voucher is deactivated.", code: "voucher_inactive");
}
