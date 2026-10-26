using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Domain.Events;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Events;

public static class TicketTypesEndpoints
{
    public static RouteGroupBuilder MapTicketTypes(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/events/{eventId:guid}/tickets").WithTags("Events");
        g.MapPost("/", Create).RequireAuthorization(Policies.EventsManage).Validate<TicketTypeRequest>();
        g.MapPut("/{ticketId:guid}", Update).RequireAuthorization(Policies.EventsManage).Validate<TicketTypeRequest>();
        g.MapPost("/{ticketId:guid}/deactivate", Deactivate).RequireAuthorization(Policies.EventsManage);
        return g;
    }

    private static async Task<IResult> Create(
        Guid eventId, TicketTypeRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var ev = await db.Events.AsNoTracking().SingleOrDefaultAsync(e => e.Id == eventId, ct);
        if (ev is null) return TypedResults.NotFound();

        var now = clock.GetUtcNow();
        var next = (await db.TicketTypes.Where(t => t.EventId == eventId).MaxAsync(t => (int?)t.SortOrder, ct) ?? 0) + 1;
        var ticket = TicketType.Create(eventId, req.Name, req.Price!.Value, req.Quantity!.Value, req.GroupSize ?? 1,
            req.SalesStartsAt, req.SalesEndsAt, req.Visibility ?? TicketVisibility.Public, next, now);
        db.TicketTypes.Add(ticket);
        audit.Record(me.Id, "ticket_type.create", nameof(TicketType), ticket.Id.ToString(), after: EventViews.Audit(ticket));
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/events/{eventId}/tickets/{ticket.Id}",
            EventViews.Ticket(ticket, EventUsageCounts.None, ev.EndsAt, now));
    }

    private static async Task<IResult> Update(
        Guid eventId, Guid ticketId, TicketTypeRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock,
        CancellationToken ct)
    {
        var ev = await db.Events.AsNoTracking().SingleOrDefaultAsync(e => e.Id == eventId, ct);
        var ticket = await db.TicketTypes.SingleOrDefaultAsync(t => t.Id == ticketId && t.EventId == eventId, ct);
        if (ev is null || ticket is null) return TypedResults.NotFound();
        if (!ticket.IsActive) return Inactive();

        var usage = await EventUsage.ForEventAsync(db, eventId, ct);
        var sold = usage.TicketSold(ticket.Id);
        var price = req.Price!.Value;
        var quantity = req.Quantity!.Value;
        var groupSize = req.GroupSize ?? 1;
        switch (ticket.CheckUpdate(price, quantity, groupSize, sold))
        {
            case TicketUpdateError.PriceLocked:
                return Problems.Result(StatusCodes.Status409Conflict, "The price can't change after tickets have been sold.", code: "price_locked");
            case TicketUpdateError.GroupSizeLocked:
                return Problems.Result(StatusCodes.Status409Conflict, "The group size can't change after tickets have been sold.", code: "group_size_locked");
            case TicketUpdateError.QuantityBelowSold:
                return Problems.Field("quantity", $"Quantity can't be below the {sold} already sold.", "quantity_below_sold");
        }

        var before = EventViews.Audit(ticket);
        ticket.Update(req.Name, price, quantity, groupSize, req.SalesStartsAt, req.SalesEndsAt, req.Visibility ?? TicketVisibility.Public, sold);
        audit.Record(me.Id, "ticket_type.update", nameof(TicketType), ticket.Id.ToString(), before, EventViews.Audit(ticket));
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(EventViews.Ticket(ticket, usage, ev.EndsAt, clock.GetUtcNow()));
    }

    private static async Task<IResult> Deactivate(
        Guid eventId, Guid ticketId, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var ev = await db.Events.AsNoTracking().SingleOrDefaultAsync(e => e.Id == eventId, ct);
        var ticket = await db.TicketTypes.SingleOrDefaultAsync(t => t.Id == ticketId && t.EventId == eventId, ct);
        if (ev is null || ticket is null) return TypedResults.NotFound();
        if (!ticket.IsActive) return Inactive();

        var now = clock.GetUtcNow();
        var before = EventViews.Audit(ticket);
        ticket.Deactivate(now);
        audit.Record(me.Id, "ticket_type.deactivate", nameof(TicketType), ticket.Id.ToString(), before, EventViews.Audit(ticket));
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(EventViews.Ticket(ticket, await EventUsage.ForEventAsync(db, eventId, ct), ev.EndsAt, now));
    }

    private static IResult Inactive() =>
        Problems.Result(StatusCodes.Status409Conflict, "This ticket type is deactivated.", code: "ticket_inactive");
}
