using Htn.Domain.Common;

namespace Htn.Domain.Registrations;

/// <summary>
/// Money received for a registration, recorded when an admin marks it paid (or automatically for a zero total).
/// A mistake is <see cref="Revert">reverted</see> with a reason, never deleted; a registration has at most one
/// payment that is not reverted. Phase 5 posts these to the event fund's ledger.
/// </summary>
public sealed class Payment
{
    public const int ReferenceMaxLength = 100;

    private Payment() { }

    public Guid Id { get; private set; }
    public Guid RegistrationId { get; private set; }
    public PaymentMethod Method { get; private set; }
    public Guid? PaymentAccountId { get; private set; }
    public string? Reference { get; private set; }

    /// <summary>PHP received. 0 for <see cref="PaymentMethod.Comp"/>.</summary>
    public decimal Amount { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset MarkedAt { get; private set; }

    /// <summary>The admin who marked it paid; null when a zero-total public checkout was paid automatically.</summary>
    public Guid? MarkedById { get; private set; }

    public DateTimeOffset? RevertedAt { get; private set; }
    public Guid? RevertedById { get; private set; }
    public string? RevertReason { get; private set; }

    public bool IsReverted => RevertedAt is not null;

    internal static Payment Create(
        Guid registrationId, PaymentMethod method, Guid? paymentAccountId, string? reference, decimal amount,
        DateTimeOffset receivedAt, DateTimeOffset at, Guid? markedById)
    {
        if (!Enum.IsDefined(method)) throw new DomainException("Unknown payment method.");
        if (amount < 0 || decimal.Round(amount, 2) != amount) throw new DomainException("Invalid amount.");
        if (method == PaymentMethod.Comp && (amount != 0 || paymentAccountId is not null))
            throw new DomainException("A comp has no amount and no payment account.");
        var r = reference?.Trim();
        if (r is { Length: > ReferenceMaxLength }) throw new DomainException($"Reference must be at most {ReferenceMaxLength} characters.");
        return new Payment
        {
            Id = Guid.CreateVersion7(at),
            RegistrationId = registrationId,
            Method = method,
            PaymentAccountId = paymentAccountId,
            Reference = string.IsNullOrEmpty(r) ? null : r,
            Amount = amount,
            ReceivedAt = receivedAt.ToUniversalTime(),
            MarkedAt = at,
            MarkedById = markedById,
        };
    }

    internal void Revert(string reason, Guid? actorId, DateTimeOffset at)
    {
        if (IsReverted) throw new DomainException("This payment is already reverted.");
        RevertedAt = at;
        RevertedById = actorId;
        RevertReason = reason;
    }
}

/// <summary>A proof-of-payment image uploaded by the registrant (never served publicly). Kept, never deleted; the newest one is shown.</summary>
public sealed class PaymentProof
{
    private PaymentProof() { }

    public Guid Id { get; private set; }
    public Guid RegistrationId { get; private set; }
    public Guid FileId { get; private set; }

    /// <summary>The payment reference as typed by the registrant (GCash ref no., bank ref).</summary>
    public string? Reference { get; private set; }

    public DateTimeOffset UploadedAt { get; private set; }

    public static PaymentProof Create(Guid registrationId, Guid fileId, string? reference, DateTimeOffset at)
    {
        var r = reference?.Trim();
        if (r is { Length: > RegistrationForm.ReferenceMaxLength })
            throw new DomainException($"Reference must be at most {RegistrationForm.ReferenceMaxLength} characters.");
        return new PaymentProof
        {
            Id = Guid.CreateVersion7(at),
            RegistrationId = registrationId,
            FileId = fileId,
            Reference = string.IsNullOrEmpty(r) ? null : r,
            UploadedAt = at,
        };
    }
}
