using Htn.Api.Features.Events;
using Htn.Domain.Common;
using Htn.Domain.Events;
using Htn.Domain.Registrations;
using Htn.Domain.Settings;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Identity;
using Htn.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Htn.Api.Features.Registrations;

public enum CheckoutError
{
    None,

    /// <summary>The ticket type is not on sale to this caller (inactive, hidden, outside its sales window, group off).</summary>
    TicketUnavailable,

    /// <summary>Event capacity or the ticket quantity would be exceeded.</summary>
    SoldOut,

    /// <summary>Unknown, inactive, expired, used-up or out-of-scope voucher. Callers never learn which.</summary>
    InvalidVoucher,

    /// <summary>attendees.length != the ticket's group size.</summary>
    GroupSizeMismatch,

    /// <summary>Event not open for this kind of registration (ended, or manual registration turned off).</summary>
    RegistrationClosed,

    /// <summary>mark-paid named a payment account that doesn't exist, is inactive, or doesn't match the method.</summary>
    InvalidPaymentAccount,
}

public sealed record CheckoutInput(
    Guid EventId, Guid TicketTypeId, string? VoucherCode, IReadOnlyList<AttendeeDetails> People, RegistrationSource Source,
    Guid? ActorId, Guid? IdempotencyKey, MarkPaidInput? MarkPaid);

/// <summary><see cref="Replayed"/>: an earlier checkout with the same idempotency key, returned as it was.</summary>
public sealed record CheckoutResult(Registration? Registration, CheckoutError Error, bool Replayed = false);

/// <summary>
/// Creates a registration. Capacity, ticket quantity and voucher limits are checked and the rows written in one
/// transaction that holds a row lock on the event (<c>SELECT … FOR UPDATE</c>), so concurrent checkouts for the same event
/// run one after another and can't oversell. Participant IDs are random and checked against the event's existing
/// IDs under the same lock; the unique index is the backstop, retried with fresh IDs.
/// </summary>
public sealed class Checkout(
    HtnDbContext db, IPublicIdGenerator ids, IAuditLog audit, TimeProvider clock, ILogger<Checkout> logger)
{
    private const int MaxAttempts = 3;

    public async Task<CheckoutResult> RunAsync(CheckoutInput input, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var result = await AttemptAsync(input, ct);
                if (result.Registration is null || result.Replayed)
                {
                    await tx.RollbackAsync(ct);
                    return result;
                }
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return result;
            }
            catch (DbUpdateException ex) when (attempt < MaxAttempts && IsRetryable(ex))
            {
                // A participant ID collided with a row written outside the lock, or a duplicate idempotency key raced in.
                logger.LogWarning("Checkout for event {EventId} hit {Constraint}; retrying", input.EventId,
                    (ex.InnerException as PostgresException)?.ConstraintName);
                await tx.RollbackAsync(ct);
                db.ChangeTracker.Clear();
            }
        }
    }

    private static bool IsRetryable(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && pg.ConstraintName is "ux_attendees_event_participant_id" or "ux_registrations_event_idempotency_key";

    private async Task<CheckoutResult> AttemptAsync(CheckoutInput input, CancellationToken ct)
    {
        // Serializes every checkout of this event until commit. Counts read below are current (READ COMMITTED takes a
        // fresh snapshot per statement) and can't change before this transaction ends.
        var locked = await db.Events.FromSql($"SELECT *, xmin FROM events WHERE id = {input.EventId} FOR UPDATE").AsNoTracking().ToListAsync(ct);
        if (locked is not [var ev]) return new CheckoutResult(null, CheckoutError.RegistrationClosed);

        if (input.IdempotencyKey is { } key)
        {
            var earlier = await db.Registrations.AsNoTracking().SingleOrDefaultAsync(r => r.EventId == ev.Id && r.IdempotencyKey == key, ct);
            if (earlier is not null) return new CheckoutResult(earlier, CheckoutError.None, Replayed: true);
        }

        // Postgres keeps microseconds: drop the last tick digit so the response matches what is stored (idempotent replays).
        var clockNow = clock.GetUtcNow();
        var now = new DateTimeOffset(clockNow.Ticks - clockNow.Ticks % 10, TimeSpan.Zero);
        var isPublic = input.Source == RegistrationSource.Public;
        var status = ev.StatusAt(now);
        if (isPublic && !(status == EventStatus.OnSale && ev.PublicPage)) return new CheckoutResult(null, CheckoutError.RegistrationClosed);
        if (!isPublic && (status == EventStatus.Ended || !ev.ManualRegistration)) return new CheckoutResult(null, CheckoutError.RegistrationClosed);

        var ticket = await db.TicketTypes.AsNoTracking().SingleOrDefaultAsync(t => t.Id == input.TicketTypeId && t.EventId == ev.Id, ct);
        if (ticket is null || !ticket.IsActive) return new CheckoutResult(null, CheckoutError.TicketUnavailable);
        if (isPublic && !PublicRegistrationViews.IsOnSalePublicly(ticket, ev, now)) return new CheckoutResult(null, CheckoutError.TicketUnavailable);
        if (input.People.Count != ticket.GroupSize) return new CheckoutResult(null, CheckoutError.GroupSizeMismatch);

        var usage = await EventUsage.ForEventAsync(db, ev.Id, ct);
        Voucher? voucher = null;
        if (!string.IsNullOrWhiteSpace(input.VoucherCode))
        {
            var code = Voucher.NormalizeCode(input.VoucherCode);
            voucher = await db.Vouchers.AsNoTracking().SingleOrDefaultAsync(v => v.EventId == ev.Id && v.Code == code, ct);
            if (voucher is null || voucher.Check(ticket.Id, usage.VoucherUsed(voucher.Id), now) != VoucherError.None)
                return new CheckoutResult(null, CheckoutError.InvalidVoucher);
        }

        if (usage.EventSold(ev.Id) + ticket.GroupSize > ev.Capacity || usage.TicketSold(ticket.Id) + 1 > ticket.Quantity)
            return new CheckoutResult(null, CheckoutError.SoldOut);

        if (input.MarkPaid is { } pay && await PaymentAccountError(db, pay.Method, pay.PaymentAccountId, ct))
            return new CheckoutResult(null, CheckoutError.InvalidPaymentAccount);

        var participantIds = await NewParticipantIdsAsync(ev.Id, input.People.Count, ct);
        var hours = ev.PaymentDeadlineHours ?? await db.OrgSettings.AsNoTracking()
            .Where(s => s.Id == OrgSettings.SingletonId).Select(s => s.PaymentDeadlineHours).SingleAsync(ct);
        var reg = Registration.Create(ev.Id, ticket, input.People, participantIds, voucher, input.Source, input.ActorId,
            RegistrationForm.ConsentVersion, now.AddHours(hours), input.IdempotencyKey, now);
        db.Registrations.Add(reg);
        audit.Record(input.ActorId, "registration.create", nameof(Registration), reg.Id.ToString(), after: RegistrationAudit.Snapshot(reg));

        if (reg.Total == 0 || input.MarkPaid is not null)
        {
            var m = input.MarkPaid;
            var payment = reg.MarkPaid(m?.Method ?? PaymentMethod.Comp, m?.PaymentAccountId, m?.Reference, m?.ReceivedAt, input.ActorId, now);
            db.Payments.Add(payment);
            audit.Record(input.ActorId, "registration.mark_paid", nameof(Registration), reg.Id.ToString(),
                new { status = RegistrationStatus.PendingPayment }, RegistrationAudit.Payment(reg, payment));
            RegistrationOutbox.QueueTickets(db, reg, now);
        }
        else
        {
            RegistrationOutbox.QueueBilling(db, ids, reg, ev.ProofMode, now);
        }
        return new CheckoutResult(reg, CheckoutError.None);
    }

    /// <summary>Random IDs that no attendee of this event has yet (and that differ from each other).</summary>
    private async Task<List<string>> NewParticipantIdsAsync(Guid eventId, int count, CancellationToken ct)
    {
        var result = new List<string>(count);
        for (var round = 0; result.Count < count; round++)
        {
            if (round > 20) throw new InvalidOperationException("Could not generate unique participant IDs.");
            var candidates = Enumerable.Range(0, count - result.Count).Select(_ => ids.NewParticipantId())
                .Where(c => !result.Contains(c)).Distinct().ToList();
            var taken = await db.Attendees.AsNoTracking()
                .Where(a => a.EventId == eventId && candidates.Contains(a.ParticipantId)).Select(a => a.ParticipantId).ToListAsync(ct);
            result.AddRange(candidates.Except(taken));
        }
        return result;
    }

    /// <summary>True when the account is unknown, inactive, or of a different kind than the method (comps take none).</summary>
    public static async Task<bool> PaymentAccountError(HtnDbContext db, PaymentMethod? method, Guid? accountId, CancellationToken ct)
    {
        if (accountId is not { } id) return false;
        if (method == PaymentMethod.Comp) return true;
        var account = await db.PaymentAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id && a.IsActive, ct);
        if (account is null) return true;
        return method switch
        {
            PaymentMethod.GCash => account.Kind != PaymentAccountKind.GCash,
            PaymentMethod.Bank => account.Kind != PaymentAccountKind.Bank,
            PaymentMethod.Cash => account.Kind != PaymentAccountKind.Cash,
            _ => true,
        };
    }
}

/// <summary>Emails a registration change queues (written in the same SaveChanges as the change).</summary>
internal static class RegistrationOutbox
{
    /// <summary>
    /// One ticket email per attendee to their own address. For a group, the buyer also gets a copy of every member's
    /// ticket (unless the member used the buyer's address).
    /// </summary>
    public static int QueueTickets(HtnDbContext db, Registration reg, DateTimeOffset at)
    {
        var buyer = reg.Buyer;
        var queued = 0;
        foreach (var a in reg.Attendees.OrderBy(a => a.SortOrder))
        {
            db.Outbox.Add(OutboxProcessor.TicketEmail(a.Id, a.Email, a.TicketVersion, at));
            queued++;
            if (!a.IsBuyer && !string.Equals(a.Email.Trim(), buyer.Email.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                db.Outbox.Add(OutboxProcessor.TicketEmail(a.Id, buyer.Email, a.TicketVersion, at));
                queued++;
            }
        }
        return queued;
    }

    /// <summary>
    /// The billing email. In Upload mode a fresh proof link token is issued (stored hashed; any older link stops
    /// working) and handed to the outbox as its secret.
    /// </summary>
    public static void QueueBilling(HtnDbContext db, IPublicIdGenerator ids, Registration reg, ProofMode mode, DateTimeOffset at)
    {
        string? raw = null;
        if (mode == ProofMode.Upload)
        {
            raw = ids.NewToken(32);
            reg.SetProofToken(AccountTokenService.Hash(raw), at);
        }
        db.Outbox.Add(OutboxProcessor.BillingEmail(reg.Id, raw, at));
    }
}

/// <summary>What the audit log keeps about registrations: IDs, amounts and statuses, never names, emails or files.</summary>
internal static class RegistrationAudit
{
    public static object Snapshot(Registration r) => new
    {
        r.EventId, r.TicketTypeId, r.Seats, r.Status, r.Source, r.Subtotal, r.Discount, r.Total, r.VoucherCode, r.Deadline,
        r.ConsentVersion, participantIds = r.Attendees.OrderBy(a => a.SortOrder).Select(a => a.ParticipantId).ToList(),
    };

    public static object Payment(Registration r, Payment p) => new
    {
        status = r.Status, paymentId = p.Id, p.Method, p.PaymentAccountId, p.Reference, p.Amount, p.ReceivedAt,
    };
}
