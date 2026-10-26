using System.Security.Cryptography;
using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Domain.Events;
using Htn.Domain.Files;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Events;

public static class EventsEndpoints
{
    public const string SlugIndex = "ix_events_slug";
    public const string MainEventIndex = "ux_events_main_event_year";

    public static RouteGroupBuilder MapEvents(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/events").WithTags("Events");
        g.MapGet("/", List).RequireAuthorization(Policies.EventsView);
        g.MapGet("/{id:guid}", Get).RequireAuthorization(Policies.EventsView);
        g.MapPost("/", Create).RequireAuthorization(Policies.EventsManage).Validate<CreateEventRequest>();
        g.MapPut("/{id:guid}", Update).RequireAuthorization(Policies.EventsManage).Validate<UpdateEventRequest>();
        g.MapPost("/{id:guid}/publish", Publish).RequireAuthorization(Policies.EventsManage);
        g.MapPost("/{id:guid}/close", Close).RequireAuthorization(Policies.EventsManage);
        g.MapPost("/{id:guid}/cover", UploadCover).RequireAuthorization(Policies.EventsManage);
        return g;
    }

    private static async Task<IResult> List(
        string? tab, HttpContext http, IAuthorizationService auth, HtnDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var t = string.IsNullOrWhiteSpace(tab) ? "upcoming" : tab.Trim().ToLowerInvariant();
        if (t is not ("upcoming" or "drafts" or "past" or "all"))
            return Problems.Field("tab", "Choose upcoming, drafts, past or all.");

        var manager = await EventViews.CanManageAsync(http, auth);
        var now = clock.GetUtcNow();
        var q = db.Events.AsNoTracking();
        if (!manager) q = q.Where(e => e.Status != EventStatus.Draft);
        q = t switch
        {
            // In progress counts as upcoming until it ends; Ended is derived from ends_at.
            "upcoming" => q.Where(e => e.Status != EventStatus.Draft && e.EndsAt >= now).OrderBy(e => e.StartsAt).ThenBy(e => e.Id),
            "drafts" => q.Where(e => e.Status == EventStatus.Draft).OrderBy(e => e.StartsAt).ThenBy(e => e.Id),
            "past" => q.Where(e => e.Status != EventStatus.Draft && e.EndsAt < now).OrderByDescending(e => e.StartsAt).ThenBy(e => e.Id),
            _ => q.OrderBy(e => e.StartsAt).ThenBy(e => e.Id),
        };
        var events = await q.ToListAsync(ct);
        var usage = await EventUsage.ForEventsAsync(db, events.Select(e => e.Id).ToList(), ct);
        return TypedResults.Ok(events.Select(e => EventViews.Summary(e, usage, now)).ToList());
    }

    private static async Task<IResult> Get(
        Guid id, HttpContext http, IAuthorizationService auth, HtnDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var e = await db.Events.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        var manager = await EventViews.CanManageAsync(http, auth);
        // Drafts don't exist for non-managers.
        if (e is null || (e.Status == EventStatus.Draft && !manager)) return TypedResults.NotFound();
        return TypedResults.Ok(await EventViews.DetailAsync(db, e, manager, clock.GetUtcNow(), ct));
    }

    private static async Task<IResult> Create(
        CreateEventRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        string slug;
        if (req.Slug is { } requested)
        {
            if (await db.Events.AnyAsync(e => e.Slug == requested, ct)) return DuplicateSlug();
            slug = requested;
        }
        else
        {
            slug = await FreeSlugAsync(db, EventSlug.FromName(req.Name), ct);
        }

        var now = clock.GetUtcNow();
        var ev = Event.Create(req.Name, slug, req.StartsAt!.Value, req.EndsAt!.Value, req.Venue, req.Capacity!.Value, req.Description, now, me.Id);
        db.Events.Add(ev);
        audit.Record(me.Id, "event.create", nameof(Event), ev.Id.ToString(), after: EventViews.Audit(ev));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (EventViews.IsUniqueViolation(ex, SlugIndex))
        {
            return DuplicateSlug();
        }
        return TypedResults.Created($"/api/events/{ev.Id}", await EventViews.DetailAsync(db, ev, manager: true, now, ct));
    }

    private static async Task<IResult> Update(
        Guid id, UpdateEventRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var ev = await db.Events.SingleOrDefaultAsync(e => e.Id == id, ct);
        if (ev is null) return TypedResults.NotFound();

        var slug = req.Slug ?? ev.Slug;
        if (!string.Equals(slug, ev.Slug, StringComparison.Ordinal))
        {
            if (!ev.CanChangeSlug)
                return Problems.Result(StatusCodes.Status409Conflict, "The link can only be changed while the event is a draft.", code: "slug_locked");
            if (await db.Events.AnyAsync(e => e.Slug == slug && e.Id != ev.Id, ct)) return DuplicateSlug();
        }

        var isMain = req.IsMainEvent ?? ev.IsMainEvent;
        var fee = req.IsMainEvent is null && req.MembershipFee is null ? ev.MembershipFee : req.MembershipFee;
        var year = req.IsMainEvent is null && req.MembershipYear is null ? ev.MembershipYear : req.MembershipYear;
        if (isMain && fee is null) return Problems.Field("membershipFee", "Enter the membership fee (0 if free).");
        if (isMain && year is null) return Problems.Field("membershipYear", "Enter the membership year.");
        if (isMain && await db.Events.AnyAsync(e => e.IsMainEvent && e.MembershipYear == year && e.Id != ev.Id, ct))
            return DuplicateMainEvent(year!.Value);

        var sold = (await EventUsage.ForEventAsync(db, ev.Id, ct)).EventSold(ev.Id);
        if (req.Capacity < sold)
            return Problems.Field("capacity", $"Capacity can't be below the {sold} already registered.", "capacity_below_sold");

        var before = EventViews.Audit(ev);
        var now = clock.GetUtcNow();
        ev.UpdateInfo(req.Name, slug, req.StartsAt!.Value, req.EndsAt!.Value, req.Venue, req.Capacity!.Value, req.Description, now, me.Id);
        ev.UpdateRegistrationSettings(
            req.ManualRegistration ?? ev.ManualRegistration, req.GroupRegistration ?? ev.GroupRegistration, req.PublicPage ?? ev.PublicPage,
            req.ProofMode ?? ev.ProofMode, isMain, fee, year, now, me.Id);
        audit.Record(me.Id, "event.update", nameof(Event), ev.Id.ToString(), before, EventViews.Audit(ev));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (EventViews.IsUniqueViolation(ex, SlugIndex))
        {
            return DuplicateSlug();
        }
        catch (DbUpdateException ex) when (EventViews.IsUniqueViolation(ex, MainEventIndex))
        {
            return DuplicateMainEvent(year!.Value);
        }
        return TypedResults.Ok(await EventViews.DetailAsync(db, ev, manager: true, now, ct));
    }

    private static async Task<IResult> Publish(Guid id, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var ev = await db.Events.SingleOrDefaultAsync(e => e.Id == id, ct);
        if (ev is null) return TypedResults.NotFound();
        var now = clock.GetUtcNow();
        if (ev.Status is not (EventStatus.Draft or EventStatus.Closed))
            return Problems.Result(StatusCodes.Status409Conflict, "This event is already on sale.", code: "invalid_status");
        var hasActiveTicket = await db.TicketTypes.AnyAsync(t => t.EventId == ev.Id && t.IsActive, ct);
        if (!hasActiveTicket)
            return Problems.Result(StatusCodes.Status409Conflict, "Add at least one active ticket type before putting the event on sale.", code: "no_tickets");
        if (ev.EndsAt < now)
            return Problems.Result(StatusCodes.Status409Conflict, "This event has already ended.", code: "event_ended");

        var before = ev.Status;
        ev.Publish(hasActiveTicket, now, me.Id);
        audit.Record(me.Id, "event.publish", nameof(Event), ev.Id.ToString(), new { status = before }, new { status = ev.Status });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await EventViews.DetailAsync(db, ev, manager: true, now, ct));
    }

    private static async Task<IResult> Close(Guid id, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var ev = await db.Events.SingleOrDefaultAsync(e => e.Id == id, ct);
        if (ev is null) return TypedResults.NotFound();
        if (ev.Status != EventStatus.OnSale)
            return Problems.Result(StatusCodes.Status409Conflict, "Only an event that is on sale can be closed.", code: "invalid_status");

        var now = clock.GetUtcNow();
        ev.Close(now, me.Id);
        audit.Record(me.Id, "event.close", nameof(Event), ev.Id.ToString(),
            new { status = EventStatus.OnSale }, new { status = ev.Status });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await EventViews.DetailAsync(db, ev, manager: true, now, ct));
    }

    private static async Task<IResult> UploadCover(
        Guid id, HttpRequest request, HtnDbContext db, IFileStorage storage, IAuditLog audit, ICurrentUser me, TimeProvider clock,
        CancellationToken ct)
    {
        var ev = await db.Events.SingleOrDefaultAsync(e => e.Id == id, ct);
        if (ev is null) return TypedResults.NotFound();

        var (file, error) = await Uploads.ReadSingleFileAsync(request, "file", StoredFilePurposes.EventCoverMaxBytes,
            "The cover must be 2 MB or smaller.", ct);
        if (error is not null) return error;
        // Trust the bytes, not the declared content type or file name.
        if (FileSignatures.DetectImage(file!.Bytes, allowJpeg: true) is not { } image)
            return Problems.Field("file", "Upload a PNG or JPG image.", "unsupported_type");
        if (image.Width > FileSignatures.MaxImageDimension || image.Height > FileSignatures.MaxImageDimension)
            return Problems.Field("file",
                $"The cover can be at most {FileSignatures.MaxImageDimension}×{FileSignatures.MaxImageDimension} pixels.", "image_too_large");

        var now = clock.GetUtcNow();
        var stored = StoredFile.Create(image.ContentType, file.Bytes.LongLength, Convert.ToHexStringLower(SHA256.HashData(file.Bytes)),
            StoredFilePurposes.EventCover, file.FileName, now, me.Id);
        await storage.SaveAsync(stored.Id, file.Bytes, ct);

        var before = ev.CoverFileId;
        db.StoredFiles.Add(stored);
        ev.SetCover(stored.Id, now, me.Id);
        audit.Record(me.Id, "event.cover_upload", nameof(Event), ev.Id.ToString(),
            new { fileId = before }, new { fileId = stored.Id, stored.ContentType, stored.SizeBytes, stored.Sha256, image.Width, image.Height });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await EventViews.DetailAsync(db, ev, manager: true, now, ct));
    }

    /// <summary>The generated slug, or the first of "-2", "-3"… that isn't taken.</summary>
    private static async Task<string> FreeSlugAsync(HtnDbContext db, string slug, CancellationToken ct)
    {
        var prefix = slug.Length > EventSlug.MaxLength - 6 ? slug[..(EventSlug.MaxLength - 6)] : slug;
        var taken = (await db.Events.Where(e => e.Slug.StartsWith(prefix)).Select(e => e.Slug).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        if (!taken.Contains(slug)) return slug;
        for (var n = 2; ; n++)
        {
            var candidate = EventSlug.WithSuffix(slug, n);
            if (!taken.Contains(candidate)) return candidate;
        }
    }

    private static IResult DuplicateSlug() =>
        Problems.Result(StatusCodes.Status409Conflict, "Another event already uses this link.", code: "duplicate_slug");

    private static IResult DuplicateMainEvent(int year) =>
        Problems.Result(StatusCodes.Status409Conflict, $"Another event is already the main event of {year}.", code: "duplicate_main_event");
}
