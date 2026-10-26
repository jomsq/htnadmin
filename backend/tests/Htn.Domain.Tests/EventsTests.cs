using Htn.Domain.Common;
using Htn.Domain.Events;
using Htn.Domain.Files;

namespace Htn.Domain.Tests;

public sealed class EventsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 8, 0, 0, TimeSpan.Zero);

    private static Event NewEvent(DateTimeOffset? endsAt = null, string slug = "htn-con-2026")
    {
        var end = endsAt ?? Now.AddDays(30);
        return Event.Create("HTN Con 2026", slug, end.AddHours(-10), end, "SMX Convention Center, Pasay", 400, null, Now.AddDays(-60), null);
    }

    // ---- Event status ----

    [Theory]
    [InlineData(EventStatus.Draft, 1, EventStatus.Draft)]
    [InlineData(EventStatus.Draft, -1, EventStatus.Draft)]     // a draft never becomes Ended
    [InlineData(EventStatus.OnSale, 1, EventStatus.OnSale)]
    [InlineData(EventStatus.OnSale, -1, EventStatus.Ended)]
    [InlineData(EventStatus.Closed, 1, EventStatus.Closed)]
    [InlineData(EventStatus.Closed, -1, EventStatus.Ended)]
    public void Ended_is_derived_from_the_end_date(EventStatus stored, int endsInHours, EventStatus expected) =>
        Assert.Equal(expected, Event.DeriveStatus(stored, Now.AddHours(endsInHours), Now));

    [Fact]
    public void Ended_starts_strictly_after_the_end()
    {
        Assert.Equal(EventStatus.OnSale, Event.DeriveStatus(EventStatus.OnSale, Now, Now));
        Assert.Equal(EventStatus.Ended, Event.DeriveStatus(EventStatus.OnSale, Now.AddTicks(-1), Now));
    }

    [Fact]
    public void New_event_is_a_draft_with_design_defaults()
    {
        var e = NewEvent();
        Assert.Equal(EventStatus.Draft, e.Status);
        Assert.True(e.ManualRegistration);
        Assert.True(e.PublicPage);
        Assert.Equal(ProofMode.Upload, e.ProofMode);
        Assert.False(e.IsMainEvent);
        Assert.Null(e.MembershipFee);
        Assert.Null(e.PublishedAt);
    }

    [Fact]
    public void Publish_needs_an_active_ticket()
    {
        var e = NewEvent();
        Assert.Throws<DomainException>(() => e.Publish(hasActiveTicket: false, Now, null));
        Assert.Equal(EventStatus.Draft, e.Status);
        e.Publish(hasActiveTicket: true, Now, null);
        Assert.Equal(EventStatus.OnSale, e.Status);
        Assert.Equal(Now, e.PublishedAt);
    }

    [Fact]
    public void Publish_close_and_reopen()
    {
        var e = NewEvent();
        Assert.Throws<DomainException>(() => e.Close(Now, null)); // a draft can't be closed
        e.Publish(true, Now, null);
        Assert.Throws<DomainException>(() => e.Publish(true, Now, null)); // already on sale
        e.Close(Now.AddDays(1), null);
        Assert.Equal(EventStatus.Closed, e.Status);
        e.Publish(true, Now.AddDays(2), null);
        Assert.Equal(EventStatus.OnSale, e.Status);
        Assert.Equal(Now, e.PublishedAt); // first publish is kept
    }

    [Fact]
    public void An_ended_event_cannot_be_put_on_sale()
    {
        var e = NewEvent(endsAt: Now.AddDays(-1));
        Assert.Throws<DomainException>(() => e.Publish(true, Now, null));
    }

    [Fact]
    public void Slug_can_only_change_while_draft()
    {
        var e = NewEvent();
        e.UpdateInfo(e.Name, "htn-con", e.StartsAt, e.EndsAt, e.Venue, e.Capacity, null, Now, null);
        Assert.Equal("htn-con", e.Slug);
        e.Publish(true, Now, null);
        Assert.False(e.CanChangeSlug);
        Assert.Throws<DomainException>(() => e.UpdateInfo(e.Name, "htn-con-2", e.StartsAt, e.EndsAt, e.Venue, e.Capacity, null, Now, null));
        // Same slug is fine.
        e.UpdateInfo("HTN Con", "htn-con", e.StartsAt, e.EndsAt, e.Venue, e.Capacity, null, Now, null);
        Assert.Equal("HTN Con", e.Name);
    }

    [Fact]
    public void Event_info_invariants()
    {
        Assert.Throws<DomainException>(() => Event.Create("X", "abc", Now, Now, "V", 10, null, Now, null)); // end == start
        Assert.Throws<DomainException>(() => Event.Create("X", "abc", Now, Now.AddHours(1), "V", 0, null, Now, null));
        Assert.Throws<DomainException>(() => Event.Create("X", "abc", Now, Now.AddHours(1), "V", 100_001, null, Now, null));
        Assert.Throws<DomainException>(() => Event.Create(" ", "abc", Now, Now.AddHours(1), "V", 10, null, Now, null));
        Assert.Throws<DomainException>(() => Event.Create("X", "Bad Slug", Now, Now.AddHours(1), "V", 10, null, Now, null));
    }

    [Fact]
    public void Main_event_needs_fee_and_year_and_others_clear_them()
    {
        var e = NewEvent();
        Assert.Throws<DomainException>(() => e.UpdateRegistrationSettings(true, true, true, ProofMode.Upload, true, null, 2026, Now, null));
        Assert.Throws<DomainException>(() => e.UpdateRegistrationSettings(true, true, true, ProofMode.Upload, true, 300m, null, Now, null));
        Assert.Throws<DomainException>(() => e.UpdateRegistrationSettings(true, true, true, ProofMode.Upload, true, -1m, 2026, Now, null));
        e.UpdateRegistrationSettings(false, false, false, ProofMode.ReplyEmail, true, 0m, 2026, Now, null);
        Assert.True(e.IsMainEvent);
        Assert.Equal(0m, e.MembershipFee);
        Assert.Equal(ProofMode.ReplyEmail, e.ProofMode);
        e.UpdateRegistrationSettings(true, true, true, ProofMode.Upload, false, 300m, 2026, Now, null);
        Assert.Null(e.MembershipFee);
        Assert.Null(e.MembershipYear);
    }

    // ---- Slugs ----

    [Theory]
    [InlineData("htn-con-2026", true)]
    [InlineData("abc", true)]
    [InlineData("ab", false)]
    [InlineData("HTN-con", false)]
    [InlineData("-htn", false)]
    [InlineData("htn-", false)]
    [InlineData("htn--con", false)]
    [InlineData("htn_con", false)]
    [InlineData("htn con", false)]
    [InlineData("htn-con\n", false)]
    public void Slug_rules(string slug, bool ok) => Assert.Equal(ok, EventSlug.IsValid(slug));

    [Fact]
    public void Slug_is_at_most_60_characters()
    {
        Assert.True(EventSlug.IsValid(new string('a', 60)));
        Assert.False(EventSlug.IsValid(new string('a', 61)));
    }

    [Theory]
    [InlineData("HTN Con 2026", "htn-con-2026")]
    [InlineData("  Hack Night: Cloud!  ", "hack-night-cloud")]
    [InlineData("Señor Café & Co.", "senor-cafe-co")]
    [InlineData("AI", "ai-event")]
    [InlineData("!!!", "event")]
    public void Slug_from_name(string name, string slug)
    {
        Assert.Equal(slug, EventSlug.FromName(name));
        Assert.True(EventSlug.IsValid(EventSlug.FromName(name)));
    }

    [Fact]
    public void Long_names_are_cut_at_a_word_boundary_and_suffixes_fit()
    {
        var slug = EventSlug.FromName(string.Join(' ', Enumerable.Repeat("security", 12)));
        Assert.True(EventSlug.IsValid(slug));
        Assert.DoesNotContain("--", slug, StringComparison.Ordinal);
        Assert.EndsWith("security", slug, StringComparison.Ordinal);

        var suffixed = EventSlug.WithSuffix(new string('a', 60), 12);
        Assert.Equal(60, suffixed.Length);
        Assert.EndsWith("-12", suffixed, StringComparison.Ordinal);
        Assert.True(EventSlug.IsValid(suffixed));
        Assert.Equal("htn-con-2", EventSlug.WithSuffix("htn-con", 2));
    }

    // ---- Ticket types ----

    private static TicketType Ticket(decimal price = 1500m, int quantity = 200, int groupSize = 1,
        DateTimeOffset? start = null, DateTimeOffset? end = null, TicketVisibility visibility = TicketVisibility.Public) =>
        TicketType.Create(Guid.CreateVersion7(), "Regular", price, quantity, groupSize, start, end, visibility, 1, Now);

    [Fact]
    public void Ticket_status_is_derived()
    {
        var eventEnd = Now.AddDays(30);
        Assert.Equal(TicketStatus.OnSale, Ticket().StatusAt(Now, 0, eventEnd));
        Assert.Equal(TicketStatus.Scheduled, Ticket(start: Now.AddDays(1)).StatusAt(Now, 0, eventEnd));
        Assert.Equal(TicketStatus.Ended, Ticket(end: Now.AddDays(-1)).StatusAt(Now, 0, eventEnd));
        Assert.Equal(TicketStatus.Ended, Ticket().StatusAt(Now, 0, Now.AddDays(-1))); // no window: the event's end
        Assert.Equal(TicketStatus.SoldOut, Ticket(quantity: 40).StatusAt(Now, 40, eventEnd));
        Assert.Equal(TicketStatus.Ended, Ticket(quantity: 150, end: Now.AddDays(-1)).StatusAt(Now, 150, eventEnd)); // early bird
        Assert.Equal(TicketStatus.Hidden, Ticket(price: 0m, visibility: TicketVisibility.Hidden).StatusAt(Now, 30, eventEnd));
        var off = Ticket();
        off.Deactivate(Now);
        Assert.Equal(TicketStatus.Inactive, off.StatusAt(Now, 0, eventEnd));
        Assert.NotNull(off.DeactivatedAt);
    }

    [Fact]
    public void Price_is_locked_once_sold()
    {
        var t = Ticket(price: 1500m);
        Assert.Equal(TicketUpdateError.None, t.CheckUpdate(1200m, 200, 1, sold: 0));
        Assert.Equal(TicketUpdateError.PriceLocked, t.CheckUpdate(1200m, 200, 1, sold: 1));
        Assert.Equal(TicketUpdateError.None, t.CheckUpdate(1500m, 250, 1, sold: 1)); // same price, more seats
        Assert.Throws<DomainException>(() => t.Update("Regular", 1200m, 200, 1, null, null, TicketVisibility.Public, sold: 3));
        Assert.Equal(1500m, t.Price);
        t.Update("Regular", 1200m, 200, 1, null, null, TicketVisibility.Public, sold: 0);
        Assert.Equal(1200m, t.Price);
    }

    [Fact]
    public void Quantity_and_group_size_respect_sales()
    {
        var t = Ticket(quantity: 10, groupSize: 5);
        Assert.Equal(TicketUpdateError.QuantityBelowSold, t.CheckUpdate(1500m, 2, 5, sold: 3));
        Assert.Equal(TicketUpdateError.None, t.CheckUpdate(1500m, 3, 5, sold: 3));
        Assert.Equal(TicketUpdateError.GroupSizeLocked, t.CheckUpdate(1500m, 10, 4, sold: 3));
        Assert.Equal(TicketUpdateError.None, t.CheckUpdate(1500m, 10, 4, sold: 0));
    }

    [Fact]
    public void Ticket_invariants()
    {
        Assert.Throws<DomainException>(() => Ticket(price: -1m));
        Assert.Throws<DomainException>(() => Ticket(price: 1_000_000.01m));
        Assert.Throws<DomainException>(() => Ticket(price: 10.001m));
        Assert.Throws<DomainException>(() => Ticket(quantity: 0));
        Assert.Throws<DomainException>(() => Ticket(groupSize: 51));
        Assert.Throws<DomainException>(() => Ticket(start: Now, end: Now));
        var off = Ticket();
        off.Deactivate(Now);
        Assert.Throws<DomainException>(() => off.Deactivate(Now));
        Assert.Throws<DomainException>(() => off.Update("X", 1m, 1, 1, null, null, TicketVisibility.Public, 0));
    }

    // ---- Vouchers ----

    [Theory]
    [InlineData(20, 1500, 300)]
    [InlineData(20, 800, 160)]
    [InlineData(15, 333.33, 50.00)]   // 49.9995 → 50.00
    [InlineData(12.5, 0.99, 0.12)]    // 0.12375 → 0.12
    [InlineData(50, 0.05, 0.03)]      // 0.025 → 0.03 (half away from zero, not banker's 0.02)
    [InlineData(33.33, 100, 33.33)]
    [InlineData(100, 6000, 6000)]
    [InlineData(50, 0, 0)]
    public void Percent_discount_rounds_to_centavos(decimal percent, decimal price, decimal expected) =>
        Assert.Equal(expected, Voucher.Discount(VoucherKind.Percent, percent, price));

    [Theory]
    [InlineData(300, 1500, 300)]
    [InlineData(2000, 1500, 1500)] // capped at the price
    [InlineData(300, 0, 0)]        // comp ticket
    [InlineData(0.01, 800, 0.01)]
    public void Fixed_discount_is_capped_at_the_price(decimal amount, decimal price, decimal expected) =>
        Assert.Equal(expected, Voucher.Discount(VoucherKind.Fixed, amount, price));

    private static Voucher NewVoucher(VoucherKind kind = VoucherKind.Percent, decimal value = 20m, Guid[]? scope = null,
        int? limit = null, DateTimeOffset? expires = null) =>
        Voucher.Create(Guid.CreateVersion7(), "student20", kind, value, scope ?? [], limit, expires, VoucherSource.Manual, Now);

    [Fact]
    public void Voucher_stores_code_upper_case_and_value_by_kind()
    {
        var p = NewVoucher();
        Assert.Equal("STUDENT20", p.Code);
        Assert.Equal(20m, p.PercentOff);
        Assert.Null(p.AmountOff);
        Assert.Equal(20m, p.Value);
        Assert.Equal(300m, p.DiscountFor(1500m));

        var f = NewVoucher(VoucherKind.Fixed, 300m);
        Assert.Null(f.PercentOff);
        Assert.Equal(300m, f.AmountOff);
        f.Update("partner300", VoucherKind.Percent, 10m, [], null, null, used: 0);
        Assert.Equal(10m, f.PercentOff);
        Assert.Null(f.AmountOff);
    }

    [Theory]
    [InlineData("STUDENT20", true)]
    [InlineData(" student20 ", true)]
    [InlineData("SPK-GOMEZ-7Q4X", true)]
    [InlineData("AB", false)]
    [InlineData("STUDENT 20", false)]
    [InlineData("STUDENT_20", false)]
    [InlineData("ÄBC", false)]
    public void Voucher_code_rules(string code, bool ok) => Assert.Equal(ok, Voucher.IsValidCode(code));

    [Theory]
    [InlineData(VoucherKind.Percent, 1, true)]
    [InlineData(VoucherKind.Percent, 100, true)]
    [InlineData(VoucherKind.Percent, 12.5, true)]
    [InlineData(VoucherKind.Percent, 0.5, false)]
    [InlineData(VoucherKind.Percent, 101, false)]
    [InlineData(VoucherKind.Percent, 12.345, false)]
    [InlineData(VoucherKind.Fixed, 0.01, true)]
    [InlineData(VoucherKind.Fixed, 0, false)]
    [InlineData(VoucherKind.Fixed, -5, false)]
    [InlineData(VoucherKind.Fixed, 1000000.01, false)]
    public void Voucher_value_rules(VoucherKind kind, decimal value, bool ok) => Assert.Equal(ok, Voucher.IsValidValue(kind, value));

    [Fact]
    public void Voucher_applicability()
    {
        var student = Guid.CreateVersion7();
        var regular = Guid.CreateVersion7();
        var v = NewVoucher(scope: [student], limit: 50, expires: Now.AddDays(10));
        Assert.Equal(VoucherError.None, v.Check(student, used: 49, Now));
        Assert.Equal(VoucherError.UsedUp, v.Check(student, used: 50, Now));
        Assert.Equal(VoucherError.NotForTicket, v.Check(regular, used: 0, Now));
        Assert.Equal(VoucherError.Expired, v.Check(student, used: 0, Now.AddDays(10)));

        var any = NewVoucher();
        Assert.Equal(VoucherError.None, any.Check(regular, used: 1_000, Now)); // no limit, no expiry, every ticket

        v.Deactivate(Now);
        Assert.Equal(VoucherError.Inactive, v.Check(student, 0, Now));
        Assert.Throws<DomainException>(() => v.Deactivate(Now));
        Assert.Throws<DomainException>(() => v.Update("X1Y", VoucherKind.Percent, 10m, [], null, null, 0));
    }

    [Fact]
    public void Usage_limit_cannot_go_below_uses()
    {
        var v = NewVoucher(limit: 50);
        Assert.Throws<DomainException>(() => v.Update("STUDENT20", VoucherKind.Percent, 20m, [], 5, null, used: 6));
        v.Update("STUDENT20", VoucherKind.Percent, 20m, [], 6, null, used: 6);
        Assert.Equal(6, v.UsageLimit);
    }

    // ---- JPEG detection ----

    public static byte[] Jpeg(ushort width, ushort height, bool withSof = true, int padding = 0)
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };
        // APP0 JFIF segment (length 16).
        bytes.AddRange([0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00]);
        // DQT-like filler segment the parser must skip.
        bytes.AddRange([0xFF, 0xDB, 0x00, 0x04, 0x00, 0x00]);
        if (withSof)
        {
            bytes.AddRange([0xFF, 0xC0, 0x00, 0x11, 0x08, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 0x03,
                0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01]);
        }
        bytes.AddRange([0xFF, 0xDA, 0x00, 0x02]);
        bytes.AddRange(new byte[padding]);
        bytes.AddRange([0xFF, 0xD9]);
        return [.. bytes];
    }

    [Fact]
    public void Jpeg_size_is_read_from_the_frame_header()
    {
        Assert.True(FileSignatures.IsJpeg(Jpeg(1600, 900)));
        Assert.Equal((1600u, 900u), FileSignatures.JpegSize(Jpeg(1600, 900)));
        Assert.Equal((4097u, 1u), FileSignatures.JpegSize(Jpeg(4097, 1)));
        Assert.Equal((0u, 0u), FileSignatures.JpegSize(Jpeg(1600, 900, withSof: false)));
        Assert.Equal((0u, 0u), FileSignatures.JpegSize(Jpeg(1600, 900)[..20])); // truncated before SOF
        Assert.Equal((0u, 0u), FileSignatures.JpegSize([0xFF, 0xD8, 0xFF]));
        Assert.False(FileSignatures.IsJpeg("GIF89a"u8));
    }

    [Fact]
    public void Detect_image_trusts_bytes_and_respects_allow_jpeg()
    {
        var jpeg = Jpeg(1600, 900);
        Assert.Equal(("image/jpeg", 1600u, 900u), FileSignatures.DetectImage(jpeg, allowJpeg: true));
        Assert.Null(FileSignatures.DetectImage(jpeg, allowJpeg: false));
        Assert.Null(FileSignatures.DetectImage(Jpeg(0, 900), allowJpeg: true));
        Assert.Null(FileSignatures.DetectImage("<svg/>"u8, allowJpeg: true));
    }

    [Fact]
    public void Event_covers_may_be_2_mb_but_logos_1_mb()
    {
        var sha = new string('a', 64);
        Assert.NotNull(StoredFile.Create("image/jpeg", 2 * 1024 * 1024, sha, StoredFilePurposes.EventCover, null, Now, null));
        Assert.Throws<DomainException>(() => StoredFile.Create("image/jpeg", 2 * 1024 * 1024 + 1, sha, StoredFilePurposes.EventCover, null, Now, null));
        Assert.Throws<DomainException>(() => StoredFile.Create("image/png", 1024 * 1024 + 1, sha, StoredFilePurposes.LogoLight, null, Now, null));
        Assert.Contains(StoredFilePurposes.EventCover, StoredFilePurposes.PublicToEveryone);
        Assert.DoesNotContain(StoredFilePurposes.EventCover, StoredFilePurposes.PublicToMembers);
    }
}
