using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Api.Features.Registrations;
using Htn.Domain.CheckIn;
using Htn.Domain.Events;
using Htn.Domain.Organization;
using Htn.Domain.Registrations;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Tickets;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.CheckIn;

/// <summary>
/// Check-in desk (frames 12 and 12b). Any staff role (CheckIn.Scan); the event must be published (Draft → 404) and
/// within a day of its dates (else 409 <c>check_in_closed</c>). Every scan is logged in the append-only
/// <c>check_in_scans</c> table, Invalid and Lookup included. Only a signed QR token on /scan, or POST /manual with an
/// attendee ID, checks anyone in; a typed participant ID is a lookup.
/// </summary>
public static class CheckInEndpoints
{
    public const int RecentLimit = 20;
    public const int SearchLimit = 20;
    public static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(5);

    public static RouteGroupBuilder MapCheckIn(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/events/{eventId:guid}/check-in").WithTags("CheckIn");
        g.MapPost("/scan", Scan).RequireAuthorization(Policies.CheckInScan).RequireRateLimiting(CheckInRateLimiting.Scan)
            .Validate<ScanRequest>();
        g.MapPost("/manual", Manual).RequireAuthorization(Policies.CheckInScan).RequireRateLimiting(CheckInRateLimiting.Scan)
            .Validate<ManualCheckInRequest>();
        g.MapGet("/stats", Stats).RequireAuthorization(Policies.CheckInScan).RequireRateLimiting(CheckInRateLimiting.Read);
        g.MapGet("/recent", Recent).RequireAuthorization(Policies.CheckInScan).RequireRateLimiting(CheckInRateLimiting.Read);
        g.MapGet("/search", Search).RequireAuthorization(Policies.CheckInScan).RequireRateLimiting(CheckInRateLimiting.Search);
        return g;
    }

    /// <summary>
    /// Null when the desk may work on this event; otherwise the error. Draft and unknown events are 404 (published only:
    /// OnSale, Closed, or Ended which is derived from those). Outside StartsAt − 1 day … EndsAt + 1 day → 409
    /// <c>check_in_closed</c>, so a desk account can't read or scan an event's attendees weeks before or after it.
    /// </summary>
    private static async Task<IResult?> GateAsync(HtnDbContext db, Guid eventId, TimeProvider clock, CancellationToken ct)
    {
        var ev = await db.Events.AsNoTracking().Where(e => e.Id == eventId && e.Status != EventStatus.Draft)
            .Select(e => new { e.StartsAt, e.EndsAt }).SingleOrDefaultAsync(ct);
        if (ev is null) return TypedResults.NotFound();
        if (!Event.IsCheckInOpen(ev.StartsAt, ev.EndsAt, clock.GetUtcNow()))
            return Problems.Result(StatusCodes.Status409Conflict, "Check-in is closed",
                "Check-in opens a day before the event starts and closes a day after it ends.", ClosedCode);
        return null;
    }

    public const string ClosedCode = "check_in_closed";

    // ---------- Scan / manual ----------

    private static async Task<IResult> Scan(
        Guid eventId, ScanRequest req, HtnDbContext db, ITicketToken tickets, IAuditLog audit, ICurrentUser me, TimeProvider clock,
        CancellationToken ct)
    {
        if (await GateAsync(db, eventId, clock, ct) is { } closed) return closed;
        var station = CheckInStation.Normalize(req.Station)!;
        var payload = req.Payload!.Trim();

        // Handheld scanners type the QR text and press Enter; staff also type the short ID from the ticket email. The
        // short ID is printed and guessable, so it is never a ticket: it only looks the attendee up (result Lookup, no
        // state change). The desk then confirms who it is and checks them in with POST /manual.
        if (ParticipantIdInput.TryNormalize(payload, out var participantId))
            return TypedResults.Ok(await LookupAsync(db, eventId, participantId, station, me, clock, ct));

        // Constant-time HMAC check. A token for another event is Invalid here, even though it is genuine.
        if (!tickets.TryVerify(payload, out var token) || token.EventId != eventId)
            return TypedResults.Ok(await CheckInAsync(db, audit, eventId, null, CheckInMethod.Qr, null, station, me, clock, ct));
        return TypedResults.Ok(await CheckInAsync(db, audit, eventId, token.AttendeeId, CheckInMethod.Qr, token.TicketVersion, station, me, clock, ct));
    }

    private static async Task<IResult> Manual(
        Guid eventId, ManualCheckInRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        if (await GateAsync(db, eventId, clock, ct) is { } closed) return closed;
        var station = CheckInStation.Normalize(req.Station)!;
        return TypedResults.Ok(await CheckInAsync(db, audit, eventId, req.AttendeeId, CheckInMethod.Manual, null, station, me, clock, ct));
    }

    /// <summary>A typed participant ID: logs a Lookup (or Invalid when unknown here) and returns the card. Changes nothing.</summary>
    private static async Task<CheckInResultDto> LookupAsync(
        HtnDbContext db, Guid eventId, string participantId, string station, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var staff = me.Id ?? throw new InvalidOperationException("Check-in requires a signed-in user.");
        var attendee = await db.Attendees.AsNoTracking().SingleOrDefaultAsync(a => a.EventId == eventId && a.ParticipantId == participantId, ct);
        var result = attendee is null ? CheckInResult.Invalid : CheckInResult.Lookup;
        db.CheckInScans.Add(CheckInScan.Create(eventId, attendee?.Id, result, CheckInMethod.Manual, station, staff, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        if (attendee is null) return InvalidResult;
        var reg = await db.Registrations.AsNoTracking().SingleAsync(r => r.Id == attendee.RegistrationId, ct);
        return await ResultAsync(db, CheckInResult.Lookup, reg, reg.Attendees.Single(a => a.Id == attendee.Id), ct);
    }

    private static readonly CheckInResultDto InvalidResult = new(CheckInResult.Invalid, null, null, null, null, null, null);

    /// <summary>This attendee's own state: CheckedIn when they are in, else the registration's (Paid for a group partly in).</summary>
    internal static RegistrationStatus AttendeeStatus(RegistrationStatus registration, DateTimeOffset? checkedInAt) =>
        checkedInAt is not null ? RegistrationStatus.CheckedIn
        : registration == RegistrationStatus.CheckedIn ? RegistrationStatus.Paid
        : registration;

    private static async Task<CheckInResultDto> ResultAsync(HtnDbContext db, CheckInResult result, Registration reg, Attendee attendee, CancellationToken ct)
    {
        var ticketName = await db.TicketTypes.AsNoTracking().Where(t => t.Id == reg.TicketTypeId).Select(t => t.Name).SingleAsync(ct);
        var email = Person.NormalizeEmail(attendee.Email);
        var isMember = await db.People.AsNoTracking().AnyAsync(p => p.IsActive && p.Email == email, ct);
        var card = new CheckInAttendeeDto(attendee.FullName, ticketName, attendee.ParticipantId, attendee.ShirtSize, isMember);
        var status = AttendeeStatus(reg.Status, attendee.CheckedInAt);
        return new CheckInResultDto(result, card,
            attendee.CheckedInAt, attendee.CheckedInStation,
            reg.Status == RegistrationStatus.PendingPayment ? reg.Total : null,
            attendee.Id, status);
    }

    /// <summary>
    /// Resolves the attendee in this event, decides the result under a row lock on the registration, logs the scan and
    /// (for Welcome) records the check-in, all in one transaction. Two simultaneous scans of the same ticket, or of two
    /// people on one group checkout, queue on the lock: exactly one gets Welcome, the other sees AlreadyCheckedIn.
    /// <paramref name="ticketVersion"/> is the version in a QR token (null for a manual check-in by attendee ID).
    /// </summary>
    private static async Task<CheckInResultDto> CheckInAsync(
        HtnDbContext db, IAuditLog audit, Guid eventId, Guid? attendeeId, CheckInMethod method, int? ticketVersion, string station,
        ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var staff = me.Id ?? throw new InvalidOperationException("Check-in requires a signed-in user.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var registrationId = attendeeId is { } aid
            ? await db.Attendees.AsNoTracking().Where(a => a.Id == aid && a.EventId == eventId)
                .Select(a => (Guid?)a.RegistrationId).SingleOrDefaultAsync(ct)
            : null;
        if (registrationId is not { } rid)
        {
            db.CheckInScans.Add(CheckInScan.Create(eventId, null, CheckInResult.Invalid, method, station, staff, clock.GetUtcNow()));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return InvalidResult;
        }

        // Serialize every check-in on this checkout; the reads below run after the lock, so they see the winner's commit.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM registrations WHERE id = {rid} FOR UPDATE", ct);
        var reg = await db.Registrations.SingleAsync(r => r.Id == rid, ct);
        var attendee = reg.Attendees.Single(a => a.Id == attendeeId);
        // Postgres keeps microseconds: truncate so the Welcome card shows exactly what a later AlreadyCheckedIn returns.
        var now = clock.GetUtcNow();
        now = now.AddTicks(-(now.Ticks % 10));

        var before = new { registrationStatus = reg.Status, attendee.CheckedInAt };
        CheckInResult result;
        if (ticketVersion is { } v && v != attendee.TicketVersion && reg.Status != RegistrationStatus.Cancelled)
            result = CheckInResult.Invalid; // an older QR (payment reverted or ticket re-issued)
        else
            result = reg.CheckIn(attendee.Id, station, staff, now);

        db.CheckInScans.Add(CheckInScan.Create(eventId, attendee.Id, result, method, station, staff, now));
        if (result == CheckInResult.Welcome)
        {
            audit.Record(staff, "attendee.check_in", nameof(Attendee), attendee.Id.ToString(), before, new
            {
                registrationId = reg.Id,
                registrationStatus = reg.Status,
                attendee.ParticipantId,
                attendee.CheckedInAt,
                attendee.CheckedInStation,
                method,
            });
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        if (result == CheckInResult.Invalid) return InvalidResult;
        return await ResultAsync(db, result, reg, attendee, ct);
    }

    // ---------- Stats / recent / search ----------

    private static async Task<IResult> Stats(Guid eventId, string? station, HtnDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (await GateAsync(db, eventId, clock, ct) is { } closed) return closed;
        var since = clock.GetUtcNow() - OnlineWindow;
        var checkedIn = await db.Attendees.CountAsync(a => a.EventId == eventId && a.CheckedInAt != null, ct);
        var paid = await db.Registrations
            .Where(r => r.EventId == eventId && (r.Status == RegistrationStatus.Paid || r.Status == RegistrationStatus.CheckedIn))
            .SumAsync(r => r.Seats, ct);
        var here = CheckInStation.Normalize(station);
        var thisStation = here is null
            ? 0
            : await db.Attendees.CountAsync(a => a.EventId == eventId && a.CheckedInAt != null && a.CheckedInStation == here, ct);
        var online = await db.CheckInScans.Where(s => s.EventId == eventId && s.At >= since).Select(s => s.Station).Distinct().CountAsync(ct);
        return TypedResults.Ok(new CheckInStatsDto(checkedIn, paid, thisStation, online));
    }

    private static async Task<IResult> Recent(Guid eventId, string? station, HtnDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (await GateAsync(db, eventId, clock, ct) is { } closed) return closed;
        var q = db.CheckInScans.AsNoTracking().Where(s => s.EventId == eventId);
        if (!string.IsNullOrWhiteSpace(station))
        {
            var here = CheckInStation.Normalize(station);
            if (here is null) return Problems.Field("station", StationRules.Message);
            q = q.Where(s => s.Station == here);
        }
        var rows = await (
                from s in q.OrderByDescending(s => s.At).ThenByDescending(s => s.Id).Take(RecentLimit)
                join a in db.Attendees on s.AttendeeId equals (Guid?)a.Id into atts
                from a in atts.DefaultIfEmpty()
                select new { s.Id, s.Result, s.Method, s.Station, s.At, a.FirstName, a.MiddleName, a.LastName, ParticipantId = (string?)a.ParticipantId })
            .ToListAsync(ct);
        return TypedResults.Ok(rows
            .OrderByDescending(r => r.At).ThenByDescending(r => r.Id)
            .Select(r => new RecentScanDto(r.Id, r.FirstName is null ? null : FullName(r.FirstName, r.MiddleName, r.LastName),
                r.ParticipantId, r.Result, r.Method, r.Station, r.At))
            .ToList());
    }

    /// <summary>Shortest name search (people search by name; participant IDs have their own rule).</summary>
    public const int MinNameSearchLength = 3;

    /// <summary>
    /// Desk search. Input shaped like a participant ID ("HTN…") matches IDs only: exactly, or by a prefix of at least
    /// <see cref="ParticipantIdInput.MinPrefixLength"/> characters (dashes ignored), never as a substring, so the
    /// search can't be used to walk the ID space. Anything else is a name search of at least 3 characters.
    /// </summary>
    private static async Task<IResult> Search(Guid eventId, string? q, HtnDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (await GateAsync(db, eventId, clock, ct) is { } closed) return closed;
        var term = q?.Trim() ?? "";
        if (term.Length > 100) term = term[..100];

        var attendees = db.Attendees.AsNoTracking().Where(a => a.EventId == eventId);
        if (ParticipantIdInput.TryNormalize(term, out var exact))
        {
            attendees = attendees.Where(a => a.ParticipantId == exact);
        }
        else if (ParticipantIdInput.TryPrefix(term, out var looksLikeId, out var prefix))
        {
            // Prefix of the canonical form: letters, digits and dashes only, so nothing to escape.
            attendees = attendees.Where(a => a.ParticipantId.StartsWith(prefix));
        }
        else if (looksLikeId || term.Length < MinNameSearchLength)
        {
            return TypedResults.Ok(Array.Empty<CheckInSearchHitDto>());
        }
        else
        {
            var pattern = "%" + RegistrationsEndpoints.LikeEscape(term) + "%";
            attendees = attendees.Where(a =>
                EF.Functions.ILike(a.FirstName + " " + a.LastName, pattern, @"\")
                || EF.Functions.ILike(a.FirstName + " " + (a.MiddleName ?? "") + " " + a.LastName, pattern, @"\"));
        }

        var rows = await (
                from a in attendees
                join r in db.Registrations on a.RegistrationId equals r.Id
                join t in db.TicketTypes on r.TicketTypeId equals t.Id
                orderby a.LastName, a.FirstName, a.Id
                select new { a.Id, a.FirstName, a.MiddleName, a.LastName, a.ParticipantId, TicketName = t.Name, r.Status, a.CheckedInAt })
            .Take(SearchLimit)
            .ToListAsync(ct);

        return TypedResults.Ok(rows.Select(r => new CheckInSearchHitDto(
            r.Id, FullName(r.FirstName, r.MiddleName, r.LastName), r.ParticipantId, r.TicketName,
            AttendeeStatus(r.Status, r.CheckedInAt))).ToList());
    }

    private static string FullName(string first, string? middle, string last) => middle is null ? $"{first} {last}" : $"{first} {middle} {last}";
}
