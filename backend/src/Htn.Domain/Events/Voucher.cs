using System.Text.RegularExpressions;
using Htn.Domain.Common;

namespace Htn.Domain.Events;

/// <summary>
/// A discount code for one event. Percent vouchers store <see cref="PercentOff"/> (numeric(5,2)), fixed ones
/// <see cref="AmountOff"/> (money). Usage is counted from non-cancelled registrations. Deactivated, never deleted.
/// </summary>
public sealed partial class Voucher
{
    public const int CodeMinLength = 3;
    public const int CodeMaxLength = 30;
    public const string CodePattern = "^[A-Z0-9-]{3,30}$";
    public const decimal MinPercent = 1m;
    public const decimal MaxPercent = 100m;
    public const decimal MaxAmount = 1_000_000m;
    public const int MaxUsageLimit = 100_000;
    public const int MaxTicketScope = 50;

    [GeneratedRegex(@"^[A-Z0-9-]{3,30}\z", RegexOptions.CultureInvariant)]
    private static partial Regex CodeRegex();

    private Voucher() { }

    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }

    /// <summary>Upper-case; unique per event (case-insensitively).</summary>
    public string Code { get; private set; } = default!;

    public VoucherKind Kind { get; private set; }
    public decimal? PercentOff { get; private set; }
    public decimal? AmountOff { get; private set; }

    /// <summary>Ticket types it applies to; empty means every ticket type of the event.</summary>
    public List<Guid> TicketTypeIds { get; private set; } = [];

    public int? UsageLimit { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public bool IsActive { get; private set; }
    public VoucherSource Source { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeactivatedAt { get; private set; }

    /// <summary>The percent (1–100) or the amount off, whichever <see cref="Kind"/> uses.</summary>
    public decimal Value => Kind == VoucherKind.Percent ? PercentOff!.Value : AmountOff!.Value;

    /// <summary>Trimmed, upper-case form used for storage and lookups.</summary>
    public static string NormalizeCode(string? code) => (code ?? "").Trim().ToUpperInvariant();

    public static bool IsValidCode(string? code) => code is not null && CodeRegex().IsMatch(NormalizeCode(code));

    public static bool IsValidValue(VoucherKind kind, decimal value) =>
        decimal.Round(value, 2) == value && kind switch
        {
            VoucherKind.Percent => value is >= MinPercent and <= MaxPercent,
            VoucherKind.Fixed => value > 0 && value <= MaxAmount,
            _ => false,
        };

    public static Voucher Create(
        Guid eventId, string code, VoucherKind kind, decimal value, IReadOnlyCollection<Guid> ticketTypeIds, int? usageLimit,
        DateTimeOffset? expiresAt, VoucherSource source, DateTimeOffset at)
    {
        if (!Enum.IsDefined(source)) throw new DomainException("Unknown voucher source.");
        var v = new Voucher { Id = Guid.CreateVersion7(at), EventId = eventId, IsActive = true, Source = source, CreatedAt = at };
        v.Apply(code, kind, value, ticketTypeIds, usageLimit, expiresAt, used: 0);
        return v;
    }

    public void Update(
        string code, VoucherKind kind, decimal value, IReadOnlyCollection<Guid> ticketTypeIds, int? usageLimit, DateTimeOffset? expiresAt, int used)
    {
        if (!IsActive) throw new DomainException("This voucher is deactivated.");
        Apply(code, kind, value, ticketTypeIds, usageLimit, expiresAt, used);
    }

    public void Deactivate(DateTimeOffset at)
    {
        if (!IsActive) throw new DomainException("This voucher is already deactivated.");
        IsActive = false;
        DeactivatedAt = at;
    }

    public bool AppliesTo(Guid ticketTypeId) => TicketTypeIds.Count == 0 || TicketTypeIds.Contains(ticketTypeId);

    /// <summary>Whether this voucher can be used for <paramref name="ticketTypeId"/> now, given <paramref name="used"/> non-cancelled uses.</summary>
    public VoucherError Check(Guid ticketTypeId, int used, DateTimeOffset now)
    {
        if (!IsActive) return VoucherError.Inactive;
        if (ExpiresAt is { } exp && exp <= now) return VoucherError.Expired;
        if (!AppliesTo(ticketTypeId)) return VoucherError.NotForTicket;
        if (UsageLimit is { } limit && used >= limit) return VoucherError.UsedUp;
        return VoucherError.None;
    }

    /// <summary>The discount on <paramref name="price"/> (one unit of the ticket type).</summary>
    public decimal DiscountFor(decimal price) => Discount(Kind, Value, price);

    /// <summary>
    /// Percent: price × percent / 100, rounded to centavos (half away from zero), never more than the price.
    /// Fixed: the amount, capped at the price (a ₱300 voucher on a ₱0 comp is ₱0).
    /// </summary>
    public static decimal Discount(VoucherKind kind, decimal value, decimal price)
    {
        if (price <= 0) return 0m;
        var raw = kind switch
        {
            VoucherKind.Percent => decimal.Round(price * value / 100m, 2, MidpointRounding.AwayFromZero),
            VoucherKind.Fixed => value,
            _ => throw new DomainException("Unknown voucher kind."),
        };
        return Math.Clamp(raw, 0m, price);
    }

    private void Apply(
        string code, VoucherKind kind, decimal value, IReadOnlyCollection<Guid> ticketTypeIds, int? usageLimit, DateTimeOffset? expiresAt, int used)
    {
        var c = NormalizeCode(code);
        if (!CodeRegex().IsMatch(c)) throw new DomainException("Use 3–30 letters, digits and dashes.");
        if (!Enum.IsDefined(kind)) throw new DomainException("Unknown voucher kind.");
        if (!IsValidValue(kind, value))
            throw new DomainException(kind == VoucherKind.Percent ? "Percent must be 1 to 100." : "Amount must be more than ₱0.");
        if (usageLimit is { } limit && (limit < 1 || limit > MaxUsageLimit)) throw new DomainException($"Usage limit must be 1 to {MaxUsageLimit}.");
        if (usageLimit is { } l && l < used) throw new DomainException($"Usage limit can't be below the {used} already used.");
        var scope = ticketTypeIds.Distinct().ToList();
        if (scope.Count > MaxTicketScope || scope.Contains(Guid.Empty)) throw new DomainException("Choose valid ticket types.");
        Code = c;
        Kind = kind;
        PercentOff = kind == VoucherKind.Percent ? value : null;
        AmountOff = kind == VoucherKind.Fixed ? value : null;
        TicketTypeIds = scope;
        UsageLimit = usageLimit;
        ExpiresAt = expiresAt?.ToUniversalTime();
    }
}
