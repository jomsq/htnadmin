using FluentValidation;
using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Domain.Events;
using Htn.Domain.Settings;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Email;
using Htn.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Events;

public sealed record EmailSettingsDefaultsDto(string AccentColor, int PaymentDeadlineHours);

/// <summary>Null/empty values fall back to <see cref="Defaults"/> (the organization's branding and deadline; all active accounts).</summary>
public sealed record EmailSettingsDto(
    string? AccentColor, IReadOnlyList<Guid> PaymentAccountIds, int? PaymentDeadlineHours, EmailSettingsDefaultsDto Defaults);

public sealed record EmailSettingsRequest(string? AccentColor, IReadOnlyList<Guid>? PaymentAccountIds, int? PaymentDeadlineHours);

public sealed record TestEmailRequest(string? Template);

public sealed record TestEmailQueuedDto(string To);

public sealed class EmailSettingsRequestValidator : AbstractValidator<EmailSettingsRequest>
{
    public const int MaxAccounts = 10;

    public EmailSettingsRequestValidator()
    {
        RuleFor(x => x.AccentColor).Must(c => c is null || ColorContrast.IsHex(c)).WithMessage("Use a hex color like #B71C1C.")
            .Must(c => c is null || !ColorContrast.IsHex(c) || ColorContrast.HasEnoughContrastWithWhite(c))
            .WithMessage("This color is too light for white text. Choose one with at least 4.5:1 contrast.");
        RuleFor(x => x.PaymentAccountIds)
            .Must(ids => ids is null || ids.Count <= MaxAccounts).WithMessage($"Choose at most {MaxAccounts} accounts.")
            .Must(ids => ids is null || !ids.Contains(Guid.Empty)).WithMessage("Choose valid accounts.");
        RuleFor(x => x.PaymentDeadlineHours)
            .InclusiveBetween(OrgSettings.MinPaymentDeadlineHours, OrgSettings.MaxPaymentDeadlineHours)
            .WithMessage($"Use {OrgSettings.MinPaymentDeadlineHours} to {OrgSettings.MaxPaymentDeadlineHours} hours.");
    }
}

public sealed class TestEmailRequestValidator : AbstractValidator<TestEmailRequest>
{
    public TestEmailRequestValidator()
    {
        RuleFor(x => x.Template).Must(t => t is RegistrationEmailTypes.TestBilling or RegistrationEmailTypes.TestTicket)
            .WithMessage("Choose billing or ticket.");
    }
}

/// <summary>Per-event billing/ticket email settings (frame 11). The layout is fixed; only these values change.</summary>
public static class EmailSettingsEndpoints
{
    public static RouteGroupBuilder MapEventEmailSettings(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/events/{eventId:guid}/email-settings").WithTags("Events");
        g.MapGet("/", Get).RequireAuthorization(Policies.EventsManage);
        g.MapPut("/", Update).RequireAuthorization(Policies.EventsManage).Validate<EmailSettingsRequest>();
        g.MapPost("/test", SendTest).RequireAuthorization(Policies.EventsManage).Validate<TestEmailRequest>();
        return g;
    }

    private static async Task<EmailSettingsDto> DtoAsync(HtnDbContext db, Event ev, CancellationToken ct)
    {
        var org = await db.OrgSettings.AsNoTracking().SingleAsync(s => s.Id == OrgSettings.SingletonId, ct);
        return new EmailSettingsDto(ev.EmailAccentColor, ev.EmailPaymentAccountIds, ev.PaymentDeadlineHours,
            new EmailSettingsDefaultsDto(OrgSettings.SafeBrandColor(org.BrandColor), org.PaymentDeadlineHours));
    }

    private static async Task<IResult> Get(Guid eventId, HtnDbContext db, CancellationToken ct)
    {
        var ev = await db.Events.AsNoTracking().SingleOrDefaultAsync(e => e.Id == eventId, ct);
        return ev is null ? TypedResults.NotFound() : TypedResults.Ok(await DtoAsync(db, ev, ct));
    }

    private static async Task<IResult> Update(
        Guid eventId, EmailSettingsRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var ev = await db.Events.SingleOrDefaultAsync(e => e.Id == eventId, ct);
        if (ev is null) return TypedResults.NotFound();
        var ids = req.PaymentAccountIds?.Distinct().ToList() ?? [];
        if (ids.Count > 0 && await db.PaymentAccounts.CountAsync(a => ids.Contains(a.Id) && a.IsActive, ct) != ids.Count)
            return Problems.Field("paymentAccountIds", "Choose active payment accounts.", "unknown_payment_account");

        var before = new { ev.EmailAccentColor, ev.EmailPaymentAccountIds, ev.PaymentDeadlineHours };
        ev.UpdateEmailSettings(req.AccentColor, ids, req.PaymentDeadlineHours, clock.GetUtcNow(), me.Id);
        audit.Record(me.Id, "event.email_settings_update", nameof(Event), ev.Id.ToString(), before,
            new { ev.EmailAccentColor, ev.EmailPaymentAccountIds, ev.PaymentDeadlineHours });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await DtoAsync(db, ev, ct));
    }

    /// <summary>Queues a sample billing or ticket email to the signed-in admin (the ticket sample's QR is not a valid ticket).</summary>
    private static async Task<IResult> SendTest(
        Guid eventId, TestEmailRequest req, HtnDbContext db, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, ct)) return TypedResults.NotFound();
        var user = await (
            from u in db.Users.AsNoTracking()
            join p in db.People.AsNoTracking() on u.PersonId equals (Guid?)p.Id
            where u.Id == me.Id
            select new { u.Email, p.FirstName }).SingleOrDefaultAsync(ct);
        if (user?.Email is null) return Problems.Result(StatusCodes.Status409Conflict, "Your account has no email address.", code: "no_email");
        db.Outbox.Add(OutboxProcessor.EventTestEmail(new EventTestEmailPayload(eventId, req.Template!, user.Email, user.FirstName), clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        return TypedResults.Accepted((string?)null, new TestEmailQueuedDto(user.Email));
    }
}
