namespace Htn.Domain.Common;

/// <summary>
/// Asia/Manila, the organization's wall clock. Everything is stored in UTC; only reporting windows (the leaderboard's
/// calendar quarter, later finance periods) need the local calendar.
/// <para>
/// The container image may ship without tzdata, so <see cref="Zone"/> falls back to a fixed +08:00 custom zone. That
/// is exact rather than approximate: the Philippines has had no DST since 1978 and no offset change is planned, so
/// Philippine Standard Time is always UTC+8.
/// </para>
/// </summary>
public static class ManilaTime
{
    public const string ZoneId = "Asia/Manila";

    /// <summary>Philippine Standard Time, UTC+8 all year.</summary>
    public static readonly TimeSpan Offset = TimeSpan.FromHours(8);

    /// <summary>True when the lookup fell back to the fixed-offset zone because tzdata was missing.</summary>
    public static bool UsingFallbackZone { get; private set; }

    public static TimeZoneInfo Zone { get; } = Resolve();

    private static TimeZoneInfo Resolve()
    {
        // "Singapore Standard Time" is the Windows id for the same +08:00, no-DST zone.
        foreach (var id in new[] { ZoneId, "Singapore Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                // Try the next id, then the fixed-offset fallback.
            }
        }
        UsingFallbackZone = true;
        return TimeZoneInfo.CreateCustomTimeZone(ZoneId, Offset, "Philippine Standard Time", "PST");
    }

    public static DateTimeOffset ToManila(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, Zone);

    /// <summary>
    /// The calendar quarter containing <paramref name="at"/>, in Manila local time: Jan 1, Apr 1, Jul 1 or Oct 1 at
    /// 00:00 +08:00, up to (but excluding) the same instant three months later. The bounds come back as UTC instants
    /// (Npgsql only accepts zero-offset values for <c>timestamptz</c> parameters), which is the same moment in time.
    /// </summary>
    public static QuarterWindow Quarter(DateTimeOffset at)
    {
        var local = ToManila(at);
        var quarter = ((local.Month - 1) / 3) + 1;
        var startLocal = new DateTime(local.Year, ((quarter - 1) * 3) + 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        return new QuarterWindow(local.Year, quarter,
            Local(startLocal).ToUniversalTime(), Local(startLocal.AddMonths(3)).ToUniversalTime());
    }

    private static DateTimeOffset Local(DateTime unspecified) => new(unspecified, Zone.GetUtcOffset(unspecified));
}

/// <summary>A half-open instant range <c>[Start, End)</c>, in UTC, covering one calendar quarter in Manila time.</summary>
/// <param name="Year">Manila-local year of <paramref name="Start"/>.</param>
/// <param name="Quarter">1–4.</param>
public readonly record struct QuarterWindow(int Year, int Quarter, DateTimeOffset Start, DateTimeOffset End)
{
    public string Label => $"Q{Quarter} {Year}";

    public bool Contains(DateTimeOffset at) => at >= Start && at < End;
}
