using Htn.Domain.Common;
using Htn.Domain.Events;

namespace Htn.Domain.Registrations;

/// <summary>Subtotal, discount and total of one checkout (one unit of a ticket type; a group ticket is one unit).</summary>
public readonly record struct RegistrationTotals(decimal Subtotal, decimal Discount, decimal Total)
{
    /// <summary>The ticket price, the voucher's discount on it (never more than the price), and the difference.</summary>
    public static RegistrationTotals For(decimal ticketPrice, Voucher? voucher)
    {
        if (ticketPrice < 0 || decimal.Round(ticketPrice, 2) != ticketPrice) throw new DomainException("Invalid ticket price.");
        var discount = voucher?.DiscountFor(ticketPrice) ?? 0m;
        return new RegistrationTotals(ticketPrice, discount, ticketPrice - discount);
    }
}

/// <summary>
/// A checkout for one ticket type: the buyer plus, for a group ticket, every group member (<see cref="Attendees"/>).
/// Counts toward capacity while not cancelled. Never deleted: cancelled with a reason instead.
/// </summary>
public sealed class Registration
{
    private Registration() { }

    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid TicketTypeId { get; private set; }

    /// <summary>Attendees on this checkout (the ticket's group size at the time), counted against event capacity.</summary>
    public int Seats { get; private set; }

    public Guid? VoucherId { get; private set; }
    public string? VoucherCode { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal Discount { get; private set; }
    public decimal Total { get; private set; }
    public RegistrationStatus Status { get; private set; }

    /// <summary>Pay-by deadline; null when nothing is due (zero total).</summary>
    public DateTimeOffset? Deadline { get; private set; }

    public RegistrationSource Source { get; private set; }

    /// <summary>The admin who entered a manual registration; null for the public page.</summary>
    public Guid? CreatedById { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ConsentAt { get; private set; }
    public string ConsentVersion { get; private set; } = default!;

    /// <summary>Client-generated key that makes duplicate public submits return the first result. Unique per event.</summary>
    public Guid? IdempotencyKey { get; private set; }

    /// <summary>SHA-256 (hex) of the proof-upload link token; the raw token is only in the billing email.</summary>
    public string? ProofTokenHash { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }
    public DateTimeOffset? CheckedInAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public Guid? CancelledById { get; private set; }
    public string? CancelReason { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    public List<Attendee> Attendees { get; private set; } = [];

    public Attendee Buyer => Attendees.OrderBy(a => a.SortOrder).First();

    /// <summary>Counts toward capacity, ticket quantity and voucher usage.</summary>
    public bool IsActive => Status != RegistrationStatus.Cancelled;

    public bool IsOverdueAt(DateTimeOffset now) => IsOverdue(Status, Deadline, now);

    public static bool IsOverdue(RegistrationStatus status, DateTimeOffset? deadline, DateTimeOffset now) =>
        status == RegistrationStatus.PendingPayment && deadline is { } d && now > d;

    /// <summary>
    /// A new checkout. <paramref name="people"/> and <paramref name="participantIds"/> are in form order (the buyer first)
    /// and must match the ticket's <paramref name="groupSize"/>. A zero total goes straight to Paid via <see cref="MarkPaid"/>
    /// (done by the caller, so the payment row is recorded too).
    /// </summary>
    public static Registration Create(
        Guid eventId, TicketType ticket, IReadOnlyList<AttendeeDetails> people, IReadOnlyList<string> participantIds,
        Voucher? voucher, RegistrationSource source, Guid? actorId, string consentVersion, DateTimeOffset? deadline,
        Guid? idempotencyKey, DateTimeOffset at)
    {
        if (ticket.EventId != eventId) throw new DomainException("The ticket belongs to another event.");
        if (people.Count != ticket.GroupSize)
            throw new DomainException(ticket.GroupSize == 1 ? "Enter one attendee." : $"Enter all {ticket.GroupSize} group members.");
        if (participantIds.Count != people.Count || participantIds.Distinct(StringComparer.Ordinal).Count() != people.Count)
            throw new DomainException("Each attendee needs a unique participant ID.");
        if (voucher is not null && voucher.EventId != eventId) throw new DomainException("The voucher belongs to another event.");
        if (!Enum.IsDefined(source)) throw new DomainException("Unknown source.");
        if (string.IsNullOrWhiteSpace(consentVersion)) throw new DomainException("Data Privacy Act consent is required.");

        var totals = RegistrationTotals.For(ticket.Price, voucher);
        var r = new Registration
        {
            Id = Guid.CreateVersion7(at),
            EventId = eventId,
            TicketTypeId = ticket.Id,
            Seats = ticket.GroupSize,
            VoucherId = voucher?.Id,
            VoucherCode = voucher?.Code,
            Subtotal = totals.Subtotal,
            Discount = totals.Discount,
            Total = totals.Total,
            Status = RegistrationStatus.PendingPayment,
            Deadline = totals.Total == 0 ? null : deadline,
            Source = source,
            CreatedById = actorId,
            CreatedAt = at,
            ConsentAt = at,
            ConsentVersion = consentVersion,
            IdempotencyKey = idempotencyKey,
        };
        for (var i = 0; i < people.Count; i++)
            r.Attendees.Add(Attendee.Create(r.Id, eventId, i, participantIds[i], people[i], at));
        return r;
    }

    /// <summary>PendingPayment → Paid, recording what was received. Returns the payment for the caller to add.</summary>
    public Payment MarkPaid(PaymentMethod method, Guid? paymentAccountId, string? reference, DateTimeOffset? receivedAt, Guid? actorId, DateTimeOffset at)
    {
        if (Status != RegistrationStatus.PendingPayment) throw new DomainException("Only a registration pending payment can be marked paid.");
        if (Total == 0 && method != PaymentMethod.Comp) method = PaymentMethod.Comp;
        var received = receivedAt ?? at;
        if (received > at.AddMinutes(5)) throw new DomainException("The payment can't be received in the future.");
        var amount = method == PaymentMethod.Comp ? 0m : Total;
        var payment = Payment.Create(Id, method, method == PaymentMethod.Comp ? null : paymentAccountId, reference, amount, received, at, actorId);
        Status = RegistrationStatus.Paid;
        PaidAt = at;
        UpdatedAt = at;
        return payment;
    }

    /// <summary>
    /// Paid → PendingPayment (the payment was recorded by mistake). Only before anyone on the checkout has checked in.
    /// Bumps every attendee's ticket version so the QR codes already sent stop working.
    /// </summary>
    public void RevertPayment(Payment payment, string reason, Guid? actorId, DateTimeOffset at)
    {
        if (Status != RegistrationStatus.Paid) throw new DomainException("Only a paid registration can have its payment reverted.");
        if (Attendees.Any(a => a.CheckedInAt is not null)) throw new DomainException("Someone on this registration has already checked in.");
        if (payment.RegistrationId != Id) throw new DomainException("The payment belongs to another registration.");
        var r = RequireReason(reason);
        payment.Revert(r, actorId, at);
        foreach (var a in Attendees) a.BumpTicketVersion();
        Status = RegistrationStatus.PendingPayment;
        PaidAt = null;
        UpdatedAt = at;
    }

    /// <summary>
    /// PendingPayment or Paid → Cancelled with a reason. Frees the seats and the voucher use (counts skip cancelled
    /// registrations) and invalidates every QR code. A recorded payment stays as it is (refunds are handled in finance).
    /// </summary>
    public void Cancel(string reason, Guid? actorId, DateTimeOffset at)
    {
        if (Status is not (RegistrationStatus.PendingPayment or RegistrationStatus.Paid))
            throw new DomainException(Status == RegistrationStatus.Cancelled
                ? "This registration is already cancelled."
                : "A checked-in registration can't be cancelled.");
        CancelReason = RequireReason(reason);
        foreach (var a in Attendees) a.BumpTicketVersion();
        Status = RegistrationStatus.Cancelled;
        CancelledAt = at;
        CancelledById = actorId;
        ProofTokenHash = null;
        UpdatedAt = at;
    }

    /// <summary>Replaces the proof-upload token (the previous link stops working).</summary>
    public void SetProofToken(string tokenHash, DateTimeOffset at)
    {
        if (Status != RegistrationStatus.PendingPayment) throw new DomainException("Only a registration pending payment takes proof of payment.");
        if (tokenHash is not { Length: 64 }) throw new DomainException("Invalid token hash.");
        ProofTokenHash = tokenHash;
        UpdatedAt = at;
    }

    /// <summary>Proof can be uploaded while payment is pending (overdue included).</summary>
    public void EnsureAcceptsProof()
    {
        if (Status != RegistrationStatus.PendingPayment) throw new DomainException("This registration is not waiting for payment.");
    }

    private static string RequireReason(string? reason)
    {
        var r = reason?.Trim();
        if (string.IsNullOrEmpty(r)) throw new DomainException("A reason is required.");
        if (r.Length > RegistrationForm.ReasonMaxLength) throw new DomainException($"Reason must be at most {RegistrationForm.ReasonMaxLength} characters.");
        return r;
    }
}
