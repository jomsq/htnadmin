using System.Text;
using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Domain.Common;
using Htn.Domain.Events;
using Htn.Domain.Files;
using Htn.Domain.Registrations;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Htn.Api.Features.Registrations;

/// <summary>Registrations of one event (frame 09). Everything is Registrations.Manage (Board), so emails are shown in full.</summary>
public static class RegistrationsEndpoints
{
    public const int MaxPageSize = 100;

    public static RouteGroupBuilder MapRegistrations(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/events/{eventId:guid}/registrations").WithTags("Registrations");
        g.MapGet("/", List).RequireAuthorization(Policies.RegistrationsManage);
        g.MapGet("/export.csv", Export).RequireAuthorization(Policies.RegistrationsManage);
        g.MapGet("/{id:guid}", Get).RequireAuthorization(Policies.RegistrationsManage);
        g.MapGet("/{id:guid}/proof", Proof).RequireAuthorization(Policies.RegistrationsManage);
        g.MapPost("/", CreateManual).RequireAuthorization(Policies.RegistrationsManage).Validate<ManualRegistrationRequest>();
        g.MapPost("/mark-paid", MarkPaid).RequireAuthorization(Policies.RegistrationsManage).Validate<BulkMarkPaidRequest>();
        g.MapPost("/{id:guid}/cancel", Cancel).RequireAuthorization(Policies.RegistrationsManage).Validate<ReasonRequest>();
        g.MapPost("/{id:guid}/revert-payment", RevertPayment).RequireAuthorization(Policies.RegistrationsManage).Validate<ReasonRequest>();
        g.MapPost("/{id:guid}/resend-billing", ResendBilling).RequireAuthorization(Policies.RegistrationsManage);
        g.MapPost("/{id:guid}/resend-ticket", ResendTicket).RequireAuthorization(Policies.RegistrationsManage);
        return g;
    }

    // ---------- List ----------

    private static async Task<IResult> List(
        Guid eventId, string? status, bool? overdue, string? search, int? page, int? pageSize, HtnDbContext db, TimeProvider clock,
        CancellationToken ct)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, ct)) return TypedResults.NotFound();
        RegistrationStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<RegistrationStatus>(status.Trim(), ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                return Problems.Field("status", "Choose PendingPayment, Paid, CheckedIn or Cancelled.");
            statusFilter = parsed;
        }
        var p = Math.Max(1, page ?? 1);
        var size = Math.Clamp(pageSize ?? 50, 1, MaxPageSize);
        var now = clock.GetUtcNow();

        var q = Search(db.Registrations.AsNoTracking().Where(r => r.EventId == eventId), search);

        var byStatus = await q.GroupBy(r => r.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var overdueCount = await q.CountAsync(r => r.Status == RegistrationStatus.PendingPayment && r.Deadline < now, ct);
        int Count(RegistrationStatus s) => byStatus.SingleOrDefault(x => x.Key == s)?.Count ?? 0;
        var counts = new RegistrationCountsDto(byStatus.Sum(x => x.Count), Count(RegistrationStatus.PendingPayment), Count(RegistrationStatus.Paid),
            Count(RegistrationStatus.CheckedIn), Count(RegistrationStatus.Cancelled), overdueCount);

        if (statusFilter is { } sf) q = q.Where(r => r.Status == sf);
        if (overdue == true) q = q.Where(r => r.Status == RegistrationStatus.PendingPayment && r.Deadline < now);
        var total = await q.CountAsync(ct);
        var regs = await q.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).Skip((p - 1) * size).Take(size).ToListAsync(ct);
        var rows = await RowsAsync(db, eventId, regs, now, ct);
        return TypedResults.Ok(new RegistrationListDto(rows, total, p, size, counts));
    }

    /// <summary>Contains-search (escaped ILIKE) on attendee names, email and participant ID.</summary>
    private static IQueryable<Registration> Search(IQueryable<Registration> q, string? search)
    {
        var term = search?.Trim();
        if (string.IsNullOrEmpty(term)) return q;
        if (term.Length > 100) term = term[..100];
        var pattern = "%" + LikeEscape(term) + "%";
        return q.Where(r => r.Attendees.Any(a =>
            EF.Functions.ILike(a.FirstName + " " + a.LastName, pattern, @"\")
            || EF.Functions.ILike(a.FirstName + " " + (a.MiddleName ?? "") + " " + a.LastName, pattern, @"\")
            || EF.Functions.ILike(a.Email, pattern, @"\")
            || EF.Functions.ILike(a.ParticipantId, pattern, @"\")));
    }

    /// <summary>Escapes the LIKE wildcards (% and _) and the escape character itself.</summary>
    public static string LikeEscape(string term) =>
        term.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("%", @"\%", StringComparison.Ordinal).Replace("_", @"\_", StringComparison.Ordinal);

    private sealed record RowExtras(string TicketName, Payment? Payment, PaymentProof? Proof);

    private static async Task<Dictionary<Guid, RowExtras>> ExtrasAsync(HtnDbContext db, Guid eventId, IReadOnlyCollection<Registration> regs, CancellationToken ct)
    {
        var ids = regs.Select(r => r.Id).ToList();
        var tickets = await db.TicketTypes.AsNoTracking().Where(t => t.EventId == eventId).ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var payments = await db.Payments.AsNoTracking().Where(x => ids.Contains(x.RegistrationId) && x.RevertedAt == null)
            .ToDictionaryAsync(x => x.RegistrationId, ct);
        var proofs = (await db.PaymentProofs.AsNoTracking().Where(x => ids.Contains(x.RegistrationId)).ToListAsync(ct))
            .GroupBy(x => x.RegistrationId).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.UploadedAt).ThenByDescending(x => x.Id).First());
        return regs.ToDictionary(r => r.Id, r => new RowExtras(
            tickets.GetValueOrDefault(r.TicketTypeId, ""), payments.GetValueOrDefault(r.Id), proofs.GetValueOrDefault(r.Id)));
    }

    private static async Task<List<RegistrationRowDto>> RowsAsync(
        HtnDbContext db, Guid eventId, IReadOnlyList<Registration> regs, DateTimeOffset now, CancellationToken ct)
    {
        var extras = await ExtrasAsync(db, eventId, regs, ct);
        return regs.Select(r => Row(r, extras[r.Id], now)).ToList();
    }

    private static RegistrationRowDto Row(Registration r, RowExtras x, DateTimeOffset now)
    {
        var buyer = r.Buyer;
        return new RegistrationRowDto(
            r.Id, buyer.FullName, buyer.Email, r.Attendees.Count, r.Attendees.OrderBy(a => a.SortOrder).Select(a => a.ParticipantId).ToList(),
            x.TicketName, r.Total, r.Status, r.IsOverdueAt(now), x.Payment?.Method, x.Payment?.Reference ?? x.Proof?.Reference,
            x.Proof is not null, r.CreatedAt, r.Source);
    }

    // ---------- Detail ----------

    private static async Task<IResult> Get(Guid eventId, Guid id, HtnDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var reg = await db.Registrations.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id && r.EventId == eventId, ct);
        if (reg is null) return TypedResults.NotFound();
        return TypedResults.Ok(await DetailAsync(db, reg, clock.GetUtcNow(), ct));
    }

    internal static async Task<RegistrationDetailDto> DetailAsync(HtnDbContext db, Registration reg, DateTimeOffset now, CancellationToken ct)
    {
        var x = (await ExtrasAsync(db, reg.EventId, [reg], ct))[reg.Id];
        var row = Row(reg, x, now);
        var history = await db.AuditEntries.AsNoTracking()
            .Where(a => a.EntityType == nameof(Registration) && a.EntityId == reg.Id.ToString())
            .OrderBy(a => a.At).ThenBy(a => a.Id).ToListAsync(ct);
        var actorIds = history.Select(h => h.ActorId).Append(x.Payment?.MarkedById).OfType<Guid>().Distinct().ToList();
        var names = await ActorNamesAsync(db, actorIds, ct);

        PaymentDto? payment = null;
        if (x.Payment is { } pay)
        {
            var label = pay.PaymentAccountId is { } accountId
                ? await db.PaymentAccounts.AsNoTracking().Where(a => a.Id == accountId).Select(a => a.Label).SingleOrDefaultAsync(ct)
                : null;
            payment = new PaymentDto(pay.Id, pay.Method, pay.PaymentAccountId, label, pay.Reference, pay.Amount, pay.ReceivedAt, pay.MarkedAt,
                pay.MarkedById is { } by ? names.GetValueOrDefault(by) : null);
        }
        var proof = x.Proof is { } pr ? new ProofDto(pr.Reference, pr.UploadedAt, $"/api/events/{reg.EventId}/registrations/{reg.Id}/proof") : null;

        return new RegistrationDetailDto(
            row.Id, row.BuyerName, row.BuyerEmail, row.AttendeeCount, row.ParticipantIds, row.TicketName, row.Total, row.Status, row.Overdue,
            row.Method, row.Reference, row.HasProof, row.CreatedAt, row.Source,
            reg.TicketTypeId,
            reg.Attendees.OrderBy(a => a.SortOrder).Select(a => new AttendeeDto(
                a.Id, a.ParticipantId, a.FirstName, a.MiddleName, a.LastName, a.Gender, a.Email, a.Mobile, a.Occupation, a.Company,
                a.ShirtSize, a.HearAbout, a.IsBuyer, a.CheckedInAt, a.CheckedInStation)).ToList(),
            reg.Subtotal, reg.Discount, reg.VoucherCode, reg.Deadline, payment, proof, reg.CancelReason, reg.ConsentAt, reg.ConsentVersion,
            history.Select(h => new HistoryDto(h.At, h.Action.StartsWith("registration.", StringComparison.Ordinal) ? h.Action["registration.".Length..] : h.Action,
                h.ActorId is { } aid ? names.GetValueOrDefault(aid) : null, h.Reason)).ToList());
    }

    /// <summary>User ID → "First Last" of the linked person.</summary>
    private static async Task<Dictionary<Guid, string>> ActorNamesAsync(HtnDbContext db, List<Guid> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0) return [];
        var rows = await (
            from u in db.Users.AsNoTracking()
            join p in db.People.AsNoTracking() on u.PersonId equals (Guid?)p.Id
            where userIds.Contains(u.Id)
            select new { u.Id, p.FirstName, p.LastName }).ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => $"{r.FirstName} {r.LastName}");
    }

    /// <summary>The newest proof image, streamed with the same locked-down headers as /api/files (never cached, never sniffed).</summary>
    private static async Task<IResult> Proof(Guid eventId, Guid id, HttpContext http, HtnDbContext db, IFileStorage storage, CancellationToken ct)
    {
        var proof = await (
            from pr in db.PaymentProofs.AsNoTracking()
            join r in db.Registrations.AsNoTracking() on pr.RegistrationId equals r.Id
            join f in db.StoredFiles.AsNoTracking() on pr.FileId equals f.Id
            where r.Id == id && r.EventId == eventId && f.Purpose == StoredFilePurposes.PaymentProof
            orderby pr.UploadedAt descending, pr.Id descending
            select new { f.Id, f.ContentType, f.Sha256 }).FirstOrDefaultAsync(ct);
        if (proof is null) return TypedResults.NotFound();
        var stream = await storage.OpenReadAsync(proof.Id, ct);
        if (stream is null) return TypedResults.NotFound();

        var headers = http.Response.Headers;
        headers[HeaderNames.XContentTypeOptions] = "nosniff";
        headers[HeaderNames.ContentSecurityPolicy] = "default-src 'none'";
        headers[HeaderNames.CacheControl] = "private, no-store";
        headers[HeaderNames.ContentDisposition] = "inline";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
        return TypedResults.Stream(stream, proof.ContentType);
    }

    // ---------- Changes ----------

    private static async Task<IResult> CreateManual(
        Guid eventId, ManualRegistrationRequest req, HtnDbContext db, Checkout checkout, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, ct)) return TypedResults.NotFound();
        var result = await checkout.RunAsync(new CheckoutInput(
            eventId, req.TicketTypeId!.Value, req.VoucherCode, req.Attendees!.Select(RegistrationRules.ToDetails).ToList(),
            RegistrationSource.Manual, me.Id, null, req.MarkPaid), ct);
        if (result.Error != CheckoutError.None)
            return result.Error == CheckoutError.InvalidPaymentAccount
                ? Problems.Field("markPaid.paymentAccountId", "Choose an active account that matches the payment method.", "invalid_payment_account")
                : RegistrationProblems.For(result.Error);
        var reg = result.Registration!;
        return TypedResults.Created($"/api/events/{eventId}/registrations/{reg.Id}", await DetailAsync(db, reg, clock.GetUtcNow(), ct));
    }

    private static async Task<IResult> MarkPaid(
        Guid eventId, BulkMarkPaidRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, ct)) return TypedResults.NotFound();
        if (await Checkout.PaymentAccountError(db, req.Method, req.PaymentAccountId, ct))
            return Problems.Field("paymentAccountId", "Choose an active account that matches the payment method.", "invalid_payment_account");
        var now = clock.GetUtcNow();
        if (req.ReceivedAt is { } received && received > now.AddMinutes(5))
            return Problems.Field("receivedAt", "The payment can't be received in the future.");

        var ids = req.Ids!.Distinct().ToList();
        // Only registrations of this event that are still pending; anything else selected is skipped.
        var regs = await db.Registrations.Where(r => r.EventId == eventId && ids.Contains(r.Id) && r.Status == RegistrationStatus.PendingPayment)
            .ToListAsync(ct);
        foreach (var reg in regs)
        {
            var payment = reg.MarkPaid(req.Method!.Value, req.PaymentAccountId, req.Reference, req.ReceivedAt, me.Id, now);
            db.Payments.Add(payment);
            audit.Record(me.Id, "registration.mark_paid", nameof(Registration), reg.Id.ToString(),
                new { status = RegistrationStatus.PendingPayment }, RegistrationAudit.Payment(reg, payment));
            RegistrationOutbox.QueueTickets(db, reg, now);
        }
        // Two admins marking the same row: the row version makes the second save a 409 concurrent_change.
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new MarkPaidResultDto(regs.Count));
    }

    private static async Task<IResult> Cancel(
        Guid eventId, Guid id, ReasonRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var reg = await db.Registrations.SingleOrDefaultAsync(r => r.Id == id && r.EventId == eventId, ct);
        if (reg is null) return TypedResults.NotFound();
        if (reg.Status is not (RegistrationStatus.PendingPayment or RegistrationStatus.Paid))
            return Problems.Result(StatusCodes.Status409Conflict, reg.Status == RegistrationStatus.Cancelled
                ? "This registration is already cancelled." : "A checked-in registration can't be cancelled.", code: "invalid_status");
        var now = clock.GetUtcNow();
        var before = reg.Status;
        reg.Cancel(req.Reason!, me.Id, now);
        audit.Record(me.Id, "registration.cancel", nameof(Registration), reg.Id.ToString(), new { status = before }, new { status = reg.Status }, reg.CancelReason);
        db.Outbox.Add(Infrastructure.Outbox.OutboxProcessor.CancelledEmail(reg.Id, now));
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await DetailAsync(db, reg, now, ct));
    }

    private static async Task<IResult> RevertPayment(
        Guid eventId, Guid id, ReasonRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var reg = await db.Registrations.SingleOrDefaultAsync(r => r.Id == id && r.EventId == eventId, ct);
        if (reg is null) return TypedResults.NotFound();
        if (reg.Status != RegistrationStatus.Paid)
            return Problems.Result(StatusCodes.Status409Conflict, reg.Status == RegistrationStatus.CheckedIn
                ? "Someone on this registration has already checked in." : "Only a paid registration can have its payment reverted.", code: "invalid_status");
        var payment = await db.Payments.SingleOrDefaultAsync(p => p.RegistrationId == reg.Id && p.RevertedAt == null, ct);
        if (payment is null) return Problems.Result(StatusCodes.Status409Conflict, "No payment to revert.", code: "invalid_status");

        var now = clock.GetUtcNow();
        reg.RevertPayment(payment, req.Reason!, me.Id, now);
        audit.Record(me.Id, "registration.revert_payment", nameof(Registration), reg.Id.ToString(),
            new { status = RegistrationStatus.Paid, paymentId = payment.Id, payment.Method, payment.Amount },
            new { status = reg.Status, ticketVersions = reg.Attendees.OrderBy(a => a.SortOrder).Select(a => a.TicketVersion).ToList() },
            payment.RevertReason);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await DetailAsync(db, reg, now, ct));
    }

    private static async Task<IResult> ResendBilling(
        Guid eventId, Guid id, HtnDbContext db, IPublicIdGenerator ids, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var reg = await db.Registrations.SingleOrDefaultAsync(r => r.Id == id && r.EventId == eventId, ct);
        if (reg is null) return TypedResults.NotFound();
        if (reg.Status != RegistrationStatus.PendingPayment)
            return Problems.Result(StatusCodes.Status409Conflict, "Only a registration pending payment gets a billing email.", code: "not_pending");
        var ev = await db.Events.AsNoTracking().SingleAsync(e => e.Id == eventId, ct);
        var now = clock.GetUtcNow();
        // A new proof link replaces the old one (the raw token only exists in the outgoing email).
        RegistrationOutbox.QueueBilling(db, ids, reg, ev.ProofMode, now);
        audit.Record(me.Id, "registration.resend_billing", nameof(Registration), reg.Id.ToString());
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ResendTicket(
        Guid eventId, Guid id, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var reg = await db.Registrations.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id && r.EventId == eventId, ct);
        if (reg is null) return TypedResults.NotFound();
        if (reg.Status is not (RegistrationStatus.Paid or RegistrationStatus.CheckedIn))
            return Problems.Result(StatusCodes.Status409Conflict, "Only a paid registration has tickets.", code: "not_paid");
        var now = clock.GetUtcNow();
        var queued = RegistrationOutbox.QueueTickets(db, reg, now);
        audit.Record(me.Id, "registration.resend_ticket", nameof(Registration), reg.Id.ToString(), after: new { emails = queued });
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    // ---------- Export ----------

    private static async Task<IResult> Export(Guid eventId, HttpContext http, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var ev = await db.Events.AsNoTracking().SingleOrDefaultAsync(e => e.Id == eventId, ct);
        if (ev is null) return TypedResults.NotFound();
        var now = clock.GetUtcNow();
        var regs = await db.Registrations.AsNoTracking().Where(r => r.EventId == eventId).OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).ToListAsync(ct);
        var extras = await ExtrasAsync(db, eventId, regs, ct);
        var csv = RegistrationCsv.Build(regs, r => extras[r.Id].TicketName, r => extras[r.Id].Payment, now);

        audit.Record(me.Id, "registration.export", nameof(Event), eventId.ToString(), after: new { registrations = regs.Count });
        await db.SaveChangesAsync(ct);

        http.Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
        http.Response.Headers[HeaderNames.CacheControl] = "private, no-store";
        var name = $"{ev.Slug}-registrations-{now.ToOffset(TimeSpan.FromHours(8)):yyyyMMdd}.csv";
        // UTF-8 with BOM so Excel opens accented names correctly.
        return TypedResults.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv; charset=utf-8", name);
    }
}
