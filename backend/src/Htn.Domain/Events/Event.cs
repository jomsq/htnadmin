using Htn.Domain.Common;
using Htn.Domain.Settings;

namespace Htn.Domain.Events;

/// <summary>
/// An event (conference, meetup, CTF…). Status moves Draft → OnSale ⇄ Closed; Ended is derived from the end date.
/// Never deleted: a cancelled event is closed.
/// </summary>
public sealed class Event
{
    public const int NameMaxLength = 120;
    public const int VenueMaxLength = 200;
    public const int DescriptionMaxLength = 2000;
    public const int MinCapacity = 1;
    public const int MaxCapacity = 100_000;
    public const decimal MaxMembershipFee = 100_000m;
    public const int MinMembershipYear = 2000;
    public const int MaxMembershipYear = 2100;
    public const int MaxEmailPaymentAccounts = 10;

    private Event() { }

    public Guid Id { get; private set; }
    public string Slug { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }
    public string Venue { get; private set; } = default!;
    public int Capacity { get; private set; }
    public string? Description { get; private set; }
    public Guid? CoverFileId { get; private set; }

    /// <summary>Stored status: Draft, OnSale or Closed (never Ended). Use <see cref="StatusAt"/> for what to show.</summary>
    public EventStatus Status { get; private set; }

    public bool ManualRegistration { get; private set; }
    public bool GroupRegistration { get; private set; }
    public bool PublicPage { get; private set; }
    public ProofMode ProofMode { get; private set; }

    /// <summary>The yearly flagship event; membership for <see cref="MembershipYear"/> is sold at it.</summary>
    public bool IsMainEvent { get; private set; }

    public decimal? MembershipFee { get; private set; }
    public int? MembershipYear { get; private set; }

    // Per-event email overrides; null/empty falls back to OrgSettings (used by the billing/ticket emails in 3B).
    public string? EmailAccentColor { get; private set; }
    public List<Guid> EmailPaymentAccountIds { get; private set; } = [];
    public int? PaymentDeadlineHours { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedById { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public Guid? UpdatedById { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }

    public static Event Create(
        string name, string slug, DateTimeOffset startsAt, DateTimeOffset endsAt, string venue, int capacity, string? description,
        DateTimeOffset at, Guid? actorId)
    {
        var e = new Event
        {
            Id = Guid.CreateVersion7(at),
            Status = EventStatus.Draft,
            ManualRegistration = true,
            GroupRegistration = true,
            PublicPage = true,
            ProofMode = ProofMode.Upload,
            CreatedAt = at,
            CreatedById = actorId,
        };
        e.SetSlug(slug);
        e.ApplyInfo(name, startsAt, endsAt, venue, capacity, description);
        return e;
    }

    /// <summary>What the UI shows: <see cref="EventStatus.Ended"/> once an OnSale/Closed event's end date has passed.</summary>
    public EventStatus StatusAt(DateTimeOffset now) => DeriveStatus(Status, EndsAt, now);

    public static EventStatus DeriveStatus(EventStatus stored, DateTimeOffset endsAt, DateTimeOffset now) =>
        stored is EventStatus.OnSale or EventStatus.Closed && endsAt < now ? EventStatus.Ended : stored;

    /// <summary>The slug is the public link, so it can only change before the event is published.</summary>
    public bool CanChangeSlug => Status == EventStatus.Draft;

    public void UpdateInfo(
        string name, string slug, DateTimeOffset startsAt, DateTimeOffset endsAt, string venue, int capacity, string? description,
        DateTimeOffset at, Guid? actorId)
    {
        if (!string.Equals(slug, Slug, StringComparison.Ordinal))
        {
            if (!CanChangeSlug) throw new DomainException("The link can only be changed while the event is a draft.");
            SetSlug(slug);
        }
        ApplyInfo(name, startsAt, endsAt, venue, capacity, description);
        Touch(at, actorId);
    }

    public void UpdateRegistrationSettings(
        bool manualRegistration, bool groupRegistration, bool publicPage, ProofMode proofMode,
        bool isMainEvent, decimal? membershipFee, int? membershipYear, DateTimeOffset at, Guid? actorId)
    {
        if (!Enum.IsDefined(proofMode)) throw new DomainException("Unknown proof mode.");
        if (isMainEvent)
        {
            if (membershipFee is not { } fee || fee < 0 || fee > MaxMembershipFee || decimal.Round(fee, 2) != fee)
                throw new DomainException("Enter the membership fee (0 or more, in pesos and centavos).");
            if (membershipYear is not (>= MinMembershipYear and <= MaxMembershipYear))
                throw new DomainException("Enter the membership year.");
        }
        ManualRegistration = manualRegistration;
        GroupRegistration = groupRegistration;
        PublicPage = publicPage;
        ProofMode = proofMode;
        IsMainEvent = isMainEvent;
        MembershipFee = isMainEvent ? membershipFee : null;
        MembershipYear = isMainEvent ? membershipYear : null;
        Touch(at, actorId);
    }

    /// <summary>Per-event email overrides (null = use the organization's settings).</summary>
    public void UpdateEmailSettings(string? accentColor, IReadOnlyCollection<Guid> paymentAccountIds, int? deadlineHours, DateTimeOffset at, Guid? actorId)
    {
        if (accentColor is not null && !ColorContrast.IsHex(accentColor)) throw new DomainException("Use a hex color like #B71C1C.");
        if (accentColor is not null && !ColorContrast.HasEnoughContrastWithWhite(accentColor))
            throw new DomainException("The accent color needs at least 4.5:1 contrast with white text.");
        if (deadlineHours is { } h && (h < OrgSettings.MinPaymentDeadlineHours || h > OrgSettings.MaxPaymentDeadlineHours))
            throw new DomainException($"Use {OrgSettings.MinPaymentDeadlineHours} to {OrgSettings.MaxPaymentDeadlineHours} hours.");
        var accounts = paymentAccountIds.Distinct().ToList();
        if (accounts.Count > MaxEmailPaymentAccounts || accounts.Contains(Guid.Empty))
            throw new DomainException($"Choose at most {MaxEmailPaymentAccounts} payment accounts.");
        EmailAccentColor = accentColor?.ToUpperInvariant();
        EmailPaymentAccountIds = accounts;
        PaymentDeadlineHours = deadlineHours;
        Touch(at, actorId);
    }

    public void SetCover(Guid fileId, DateTimeOffset at, Guid? actorId)
    {
        CoverFileId = fileId;
        Touch(at, actorId);
    }

    /// <summary>Draft or Closed → OnSale. Needs at least one active ticket type and an end date in the future.</summary>
    public void Publish(bool hasActiveTicket, DateTimeOffset at, Guid? actorId)
    {
        if (Status is not (EventStatus.Draft or EventStatus.Closed))
            throw new DomainException("Only a draft or closed event can be put on sale.");
        if (!hasActiveTicket) throw new DomainException("Add at least one active ticket type first.");
        if (EndsAt < at) throw new DomainException("This event has already ended.");
        Status = EventStatus.OnSale;
        PublishedAt ??= at;
        Touch(at, actorId);
    }

    /// <summary>OnSale → Closed: registration stops; the event still happens.</summary>
    public void Close(DateTimeOffset at, Guid? actorId)
    {
        if (Status != EventStatus.OnSale) throw new DomainException("Only an event that is on sale can be closed.");
        Status = EventStatus.Closed;
        Touch(at, actorId);
    }

    private void SetSlug(string slug)
    {
        if (!EventSlug.IsValid(slug))
            throw new DomainException("Use 3–60 lower-case letters, digits and single dashes.");
        Slug = slug;
    }

    private void ApplyInfo(string name, DateTimeOffset startsAt, DateTimeOffset endsAt, string venue, int capacity, string? description)
    {
        var n = name?.Trim();
        if (string.IsNullOrEmpty(n)) throw new DomainException("Enter the event name.");
        if (n.Length > NameMaxLength) throw new DomainException($"Name must be at most {NameMaxLength} characters.");
        var v = venue?.Trim();
        if (string.IsNullOrEmpty(v)) throw new DomainException("Enter the venue.");
        if (v.Length > VenueMaxLength) throw new DomainException($"Venue must be at most {VenueMaxLength} characters.");
        if (endsAt <= startsAt) throw new DomainException("The end must be after the start.");
        if (capacity is < MinCapacity or > MaxCapacity) throw new DomainException($"Capacity must be {MinCapacity} to {MaxCapacity}.");
        var d = description?.Trim();
        if (d is { Length: > DescriptionMaxLength }) throw new DomainException($"Description must be at most {DescriptionMaxLength} characters.");
        Name = n;
        Venue = v;
        StartsAt = startsAt.ToUniversalTime();
        EndsAt = endsAt.ToUniversalTime();
        Capacity = capacity;
        Description = string.IsNullOrEmpty(d) ? null : d;
    }

    private void Touch(DateTimeOffset at, Guid? actorId)
    {
        UpdatedAt = at;
        UpdatedById = actorId;
    }
}
