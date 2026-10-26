using Htn.Domain.Common;

namespace Htn.Domain.Events;

public enum TicketUpdateError
{
    None,

    /// <summary>The price can't change once a ticket has been sold (registrations keep the price they paid).</summary>
    PriceLocked,

    /// <summary>The group size can't change once sold (it fixes how many attendees each checkout has).</summary>
    GroupSizeLocked,

    /// <summary>The quantity can't go below what has been sold.</summary>
    QuantityBelowSold,
}

/// <summary>
/// A ticket type of an event. <see cref="Quantity"/> counts checkouts: for a group ticket (<see cref="GroupSize"/> &gt; 1)
/// one unit is one group. Sold counts are derived from registrations, never stored. Deactivated, never deleted.
/// </summary>
public sealed class TicketType
{
    public const int NameMaxLength = 80;
    public const decimal MaxPrice = 1_000_000m;
    public const int MinQuantity = 1;
    public const int MaxQuantity = 100_000;
    public const int MinGroupSize = 1;
    public const int MaxGroupSize = 50;

    private TicketType() { }

    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public string Name { get; private set; } = default!;

    /// <summary>Price per unit (per group for group tickets), PHP.</summary>
    public decimal Price { get; private set; }

    public int Quantity { get; private set; }
    public int GroupSize { get; private set; }
    public DateTimeOffset? SalesStartsAt { get; private set; }
    public DateTimeOffset? SalesEndsAt { get; private set; }
    public TicketVisibility Visibility { get; private set; }
    public bool IsActive { get; private set; }
    public int SortOrder { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeactivatedAt { get; private set; }

    public static TicketType Create(
        Guid eventId, string name, decimal price, int quantity, int groupSize, DateTimeOffset? salesStartsAt, DateTimeOffset? salesEndsAt,
        TicketVisibility visibility, int sortOrder, DateTimeOffset at)
    {
        var t = new TicketType { Id = Guid.CreateVersion7(at), EventId = eventId, IsActive = true, SortOrder = sortOrder, CreatedAt = at };
        t.Apply(name, price, quantity, groupSize, salesStartsAt, salesEndsAt, visibility);
        return t;
    }

    public TicketUpdateError CheckUpdate(decimal price, int quantity, int groupSize, int sold)
    {
        if (sold > 0 && price != Price) return TicketUpdateError.PriceLocked;
        if (sold > 0 && groupSize != GroupSize) return TicketUpdateError.GroupSizeLocked;
        if (quantity < sold) return TicketUpdateError.QuantityBelowSold;
        return TicketUpdateError.None;
    }

    public void Update(
        string name, decimal price, int quantity, int groupSize, DateTimeOffset? salesStartsAt, DateTimeOffset? salesEndsAt,
        TicketVisibility visibility, int sold)
    {
        EnsureActive();
        switch (CheckUpdate(price, quantity, groupSize, sold))
        {
            case TicketUpdateError.PriceLocked: throw new DomainException("The price can't change after tickets have been sold.");
            case TicketUpdateError.GroupSizeLocked: throw new DomainException("The group size can't change after tickets have been sold.");
            case TicketUpdateError.QuantityBelowSold: throw new DomainException($"Quantity can't be below the {sold} already sold.");
        }
        Apply(name, price, quantity, groupSize, salesStartsAt, salesEndsAt, visibility);
    }

    public void Deactivate(DateTimeOffset at)
    {
        EnsureActive();
        IsActive = false;
        DeactivatedAt = at;
    }

    /// <summary>Status for the tickets table. Hidden/Inactive win; a window that has passed shows Ended even when sold out.</summary>
    public TicketStatus StatusAt(DateTimeOffset now, int sold, DateTimeOffset eventEndsAt) =>
        DeriveStatus(IsActive, Visibility, Quantity, sold, SalesStartsAt, SalesEndsAt, eventEndsAt, now);

    public static TicketStatus DeriveStatus(
        bool isActive, TicketVisibility visibility, int quantity, int sold,
        DateTimeOffset? salesStartsAt, DateTimeOffset? salesEndsAt, DateTimeOffset eventEndsAt, DateTimeOffset now)
    {
        if (!isActive) return TicketStatus.Inactive;
        if (visibility == TicketVisibility.Hidden) return TicketStatus.Hidden;
        if ((salesEndsAt ?? eventEndsAt) < now) return TicketStatus.Ended;
        if (sold >= quantity) return TicketStatus.SoldOut;
        if (salesStartsAt is { } start && start > now) return TicketStatus.Scheduled;
        return TicketStatus.OnSale;
    }

    private void Apply(
        string name, decimal price, int quantity, int groupSize, DateTimeOffset? salesStartsAt, DateTimeOffset? salesEndsAt,
        TicketVisibility visibility)
    {
        var n = name?.Trim();
        if (string.IsNullOrEmpty(n)) throw new DomainException("Enter the ticket name.");
        if (n.Length > NameMaxLength) throw new DomainException($"Name must be at most {NameMaxLength} characters.");
        if (price < 0 || price > MaxPrice || decimal.Round(price, 2) != price)
            throw new DomainException("Price must be ₱0 to ₱1,000,000 (centavos at most).");
        if (quantity is < MinQuantity or > MaxQuantity) throw new DomainException($"Quantity must be {MinQuantity} to {MaxQuantity}.");
        if (groupSize is < MinGroupSize or > MaxGroupSize) throw new DomainException($"Group size must be {MinGroupSize} to {MaxGroupSize}.");
        if (salesStartsAt is { } s && salesEndsAt is { } e && e <= s) throw new DomainException("Sales must end after they start.");
        if (!Enum.IsDefined(visibility)) throw new DomainException("Unknown visibility.");
        Name = n;
        Price = price;
        Quantity = quantity;
        GroupSize = groupSize;
        SalesStartsAt = salesStartsAt?.ToUniversalTime();
        SalesEndsAt = salesEndsAt?.ToUniversalTime();
        Visibility = visibility;
    }

    private void EnsureActive()
    {
        if (!IsActive) throw new DomainException("This ticket type is deactivated.");
    }
}
