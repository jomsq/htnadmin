using System.Security.Cryptography;
using FluentValidation;
using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Api.Features.Events;
using Htn.Api.Features.Settings;
using Htn.Domain.Events;
using Htn.Domain.Files;
using Htn.Domain.Registrations;
using Htn.Domain.Settings;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Identity;
using Htn.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Registrations;

internal static class PublicRegistrationViews
{
    /// <summary>A public ticket that can be bought now: active, public, in its sales window, and (for groups) group registration on.</summary>
    public static bool IsOnSalePublicly(TicketType t, Event ev, DateTimeOffset now) =>
        t.IsActive && t.Visibility == TicketVisibility.Public
        && (t.SalesStartsAt is not { } start || start <= now)
        && (t.SalesEndsAt ?? ev.EndsAt) >= now
        && (t.GroupSize == 1 || ev.GroupRegistration);

    /// <summary>Checkouts still available: ticket quantity left, limited by the seats left in the event.</summary>
    public static int Remaining(TicketType t, Event ev, EventUsageCounts usage)
    {
        var byQuantity = t.Quantity - usage.TicketSold(t.Id);
        var byCapacity = (ev.Capacity - usage.EventSold(ev.Id)) / t.GroupSize;
        return Math.Max(0, Math.Min(byQuantity, byCapacity));
    }

    /// <summary>The event the public page may show: on sale, public page on, not ended. Anything else is a 404.</summary>
    public static async Task<Event?> FindPublicAsync(HtnDbContext db, string slug, DateTimeOffset now, CancellationToken ct)
    {
        if (!EventSlug.IsValid(slug)) return null;
        var ev = await db.Events.AsNoTracking().SingleOrDefaultAsync(e => e.Slug == slug, ct);
        return ev is not null && ev.PublicPage && ev.StatusAt(now) == EventStatus.OnSale ? ev : null;
    }
}

/// <summary>
/// Anonymous endpoints for the public registration page (frame 10) and the proof upload page. Every POST goes
/// through the X-CSRF-TOKEN check (the page fetches /api/auth/antiforgery first, like login) and a per-IP rate limit;
/// registration also has a per-email limit and a honeypot field.
/// </summary>
public static class PublicRegistrationEndpoints
{
    /// <summary>Uploads kept per registration (each up to 5 MB), so a leaked link can't fill the disk.</summary>
    public const int MaxProofsPerRegistration = 10;

    public static RouteGroupBuilder MapPublicRegistration(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/public").WithTags("Public registration").AllowAnonymous().AllowWithoutTwoFactor();
        g.MapGet("/events/{slug}", GetEvent).RequireRateLimiting(PublicRateLimiting.Read);
        g.MapPost("/events/{slug}/voucher-check", CheckVoucher).RequireRateLimiting(PublicRateLimiting.Voucher).Validate<VoucherCheckRequest>();
        g.MapPost("/events/{slug}/registrations", Register).RequireRateLimiting(PublicRateLimiting.Register);
        g.MapGet("/proof/{token}", GetProof).RequireRateLimiting(PublicRateLimiting.Proof);
        g.MapPost("/proof/{token}", UploadProof).RequireRateLimiting(PublicRateLimiting.Proof);
        return g;
    }

    private static async Task<IResult> GetEvent(string slug, HtnDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var ev = await PublicRegistrationViews.FindPublicAsync(db, slug, now, ct);
        if (ev is null) return TypedResults.NotFound();

        var usage = await EventUsage.ForEventAsync(db, ev.Id, ct);
        var tickets = (await db.TicketTypes.AsNoTracking().Where(t => t.EventId == ev.Id).OrderBy(t => t.SortOrder).ThenBy(t => t.Id).ToListAsync(ct))
            .Where(t => PublicRegistrationViews.IsOnSalePublicly(t, ev, now))
            .Select(t => new PublicTicketDto(t.Id, t.Name, t.Price, t.GroupSize, PublicRegistrationViews.Remaining(t, ev, usage), null))
            .ToList();
        var org = await db.OrgSettings.AsNoTracking().SingleAsync(s => s.Id == OrgSettings.SingletonId, ct);
        var color = ev.EmailAccentColor is { } accent && ColorContrast.IsHex(accent) ? accent : OrgSettings.SafeBrandColor(org.BrandColor);
        return TypedResults.Ok(new PublicEventDto(
            ev.Name, ev.Slug, ev.StartsAt, ev.EndsAt, ev.Venue, ev.Description, EventViews.CoverUrl(ev),
            org.LogoLightFileId is { } logo ? FilesEndpoints.PublicFileUrl(logo) : null, color, ev.GroupRegistration, ev.ProofMode, tickets,
            RegistrationForm.Genders, RegistrationForm.HearAbout, RegistrationForm.ShirtSizes, RegistrationForm.ConsentText, RegistrationForm.ConsentVersion));
    }

    /// <summary>Same answer for unknown, expired, used-up, inactive and out-of-scope codes: { valid: false }.</summary>
    private static async Task<IResult> CheckVoucher(string slug, VoucherCheckRequest req, HtnDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var ev = await PublicRegistrationViews.FindPublicAsync(db, slug, now, ct);
        if (ev is null) return TypedResults.NotFound();
        var ticket = req.TicketTypeId is { } tid
            ? await db.TicketTypes.AsNoTracking().SingleOrDefaultAsync(t => t.Id == tid && t.EventId == ev.Id, ct)
            : null;
        if (ticket is null || !PublicRegistrationViews.IsOnSalePublicly(ticket, ev, now))
            return TypedResults.Ok(new VoucherCheckDto(false, 0m, 0m));

        var code = Voucher.NormalizeCode(req.Code);
        var voucher = Voucher.IsValidCode(code)
            ? await db.Vouchers.AsNoTracking().SingleOrDefaultAsync(v => v.EventId == ev.Id && v.Code == code, ct)
            : null;
        if (voucher is null) return TypedResults.Ok(new VoucherCheckDto(false, 0m, ticket.Price));
        var used = (await EventUsage.ForEventAsync(db, ev.Id, ct)).VoucherUsed(voucher.Id);
        if (voucher.Check(ticket.Id, used, now) != VoucherError.None) return TypedResults.Ok(new VoucherCheckDto(false, 0m, ticket.Price));
        var totals = RegistrationTotals.For(ticket.Price, voucher);
        return TypedResults.Ok(new VoucherCheckDto(true, totals.Discount, totals.Total));
    }

    private static async Task<IResult> Register(
        string slug, PublicRegistrationRequest? req, IValidator<PublicRegistrationRequest> validator, HtnDbContext db, Checkout checkout,
        EmailRateLimiter emailLimits, TimeProvider clock, CancellationToken ct)
    {
        if (req is null) return Problems.Result(StatusCodes.Status400BadRequest, "Request body is required.");
        // Honeypot: people never see the field; bots fill it. Nothing is stored and the reply doesn't say why.
        if (!string.IsNullOrEmpty(req.Website))
            return Problems.Result(StatusCodes.Status400BadRequest, "We couldn't process this registration.", "Refresh the page and try again.", "rejected");

        var validation = await validator.ValidateAsync(req, ct);
        if (!validation.IsValid) return TypedResults.ValidationProblem(validation.ToDictionary());

        // Counted before any lookup, so the limit is the same whatever the email.
        if (!emailLimits.TryRegister(req.Attendees![0].Email!)) return AuthRateLimiting.TooMany();

        var now = clock.GetUtcNow();
        var ev = await PublicRegistrationViews.FindPublicAsync(db, slug, now, ct);
        if (ev is null) return TypedResults.NotFound();

        var result = await checkout.RunAsync(new CheckoutInput(
            ev.Id, req.TicketTypeId!.Value, req.VoucherCode, req.Attendees.Select(RegistrationRules.ToDetails).ToList(),
            RegistrationSource.Public, null, req.IdempotencyKey, null), ct);
        if (result.Error != CheckoutError.None) return RegistrationProblems.For(result.Error);

        var reg = result.Registration!;
        var dto = new RegistrationCreatedDto(
            reg.Attendees.OrderBy(a => a.SortOrder).Select(a => a.ParticipantId).ToList(),
            reg.Status == RegistrationStatus.PendingPayment ? reg.Total : 0m,
            reg.Status == RegistrationStatus.PendingPayment ? reg.Deadline : null,
            ev.ProofMode, reg.Buyer.Email, reg.Status);
        // A replayed idempotency key gets the original response, status included.
        return TypedResults.Created((string?)null, dto);
    }

    // ---------- Proof of payment (Upload mode) ----------

    /// <summary>The registration a proof token belongs to, while it is usable: not cancelled, and before the event ends.</summary>
    private static async Task<(Registration Reg, Event Ev)?> FindByTokenAsync(HtnDbContext db, string token, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100) return null;
        var hash = AccountTokenService.Hash(token.Trim());
        var reg = await db.Registrations.SingleOrDefaultAsync(r => r.ProofTokenHash == hash, ct);
        if (reg is null || reg.Status == RegistrationStatus.Cancelled) return null;
        var ev = await db.Events.AsNoTracking().SingleAsync(e => e.Id == reg.EventId, ct);
        return ev.EndsAt > now ? (reg, ev) : null;
    }

    private static async Task<IResult> GetProof(string token, HttpContext http, HtnDbContext db, TimeProvider clock, CancellationToken ct)
    {
        http.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        http.Response.Headers.CacheControl = "no-store";
        var found = await FindByTokenAsync(db, token, clock.GetUtcNow(), ct);
        if (found is not { } f) return TypedResults.NotFound();
        var (reg, ev) = f;
        var uploaded = await db.PaymentProofs.AnyAsync(p => p.RegistrationId == reg.Id, ct);
        return TypedResults.Ok(new ProofInfoDto(ev.Name, reg.Buyer.ParticipantId, reg.Total, reg.Deadline, uploaded, reg.Status));
    }

    private static async Task<IResult> UploadProof(
        string token, HttpRequest request, HtnDbContext db, IFileStorage storage, IAuditLog audit, TimeProvider clock, CancellationToken ct)
    {
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        var now = clock.GetUtcNow();
        var found = await FindByTokenAsync(db, token, now, ct);
        if (found is not { } f) return TypedResults.NotFound();
        var reg = f.Reg;
        if (reg.Status != RegistrationStatus.PendingPayment)
            return Problems.Result(StatusCodes.Status409Conflict, "This registration is already paid.", code: "not_pending");

        if (await db.PaymentProofs.CountAsync(p => p.RegistrationId == reg.Id, ct) >= MaxProofsPerRegistration)
            return Problems.Result(StatusCodes.Status409Conflict, "Too many uploads",
                "This registration already has several proofs. Contact the organizers if you need to send another.", "too_many_uploads");

        var (file, error) = await Uploads.ReadSingleFileAsync(request, "file", StoredFilePurposes.PaymentProofMaxBytes,
            "The image must be 5 MB or smaller.", ct);
        if (error is not null) return error;
        var reference = RegistrationRules.Clean(request.Form["reference"].ToString());
        if (reference is { Length: > RegistrationForm.ReferenceMaxLength })
            return Problems.Field("reference", $"Use at most {RegistrationForm.ReferenceMaxLength} characters.");
        // Trust the bytes, never the declared type or name.
        if (FileSignatures.DetectImage(file!.Bytes, allowJpeg: true) is not { } image)
            return Problems.Field("file", "Upload a PNG or JPG image.", "unsupported_type");
        if (image.Width > StoredFilePurposes.PaymentProofMaxDimension || image.Height > StoredFilePurposes.PaymentProofMaxDimension)
            return Problems.Field("file", "The image is too large. Upload a screenshot instead.", "image_too_large");

        reg.EnsureAcceptsProof();
        var sha = Convert.ToHexStringLower(SHA256.HashData(file.Bytes));
        // The registrant's file name is not kept: it is personal data the design doesn't ask for.
        var stored = StoredFile.Create(image.ContentType, file.Bytes.LongLength, sha, StoredFilePurposes.PaymentProof, null, now, null);
        await storage.SaveAsync(stored.Id, file.Bytes, ct);
        db.StoredFiles.Add(stored);
        var proof = PaymentProof.Create(reg.Id, stored.Id, reference, now);
        db.PaymentProofs.Add(proof);
        audit.Record(null, "registration.proof_uploaded", nameof(Registration), reg.Id.ToString(),
            after: new { proofId = proof.Id, fileId = stored.Id, stored.ContentType, stored.SizeBytes, stored.Sha256, proof.Reference });
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }
}

internal static class RegistrationProblems
{
    public static IResult For(CheckoutError error) => error switch
    {
        CheckoutError.SoldOut => Problems.Result(StatusCodes.Status409Conflict, "Sold out", "There aren't enough seats left for this ticket.", "sold_out"),
        CheckoutError.TicketUnavailable => Problems.Result(StatusCodes.Status409Conflict, "Ticket not available",
            "This ticket can't be bought right now. Choose another ticket.", "ticket_unavailable"),
        CheckoutError.InvalidVoucher => Problems.Field("voucherCode", "This voucher code is not valid.", "invalid_voucher"),
        CheckoutError.GroupSizeMismatch => Problems.Field("attendees", "Enter every member of the group.", "group_size"),
        CheckoutError.InvalidPaymentAccount => Problems.Field("paymentAccountId", "Choose an active account that matches the payment method.",
            "invalid_payment_account"),
        _ => Problems.Result(StatusCodes.Status409Conflict, "Registration closed", "This event isn't taking these registrations.", "registration_closed"),
    };
}
