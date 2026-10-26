using System.Text;
using Htn.Domain.Common;
using Htn.Domain.Events;
using Htn.Domain.Registrations;

namespace Htn.Domain.Tests;

public sealed class RegistrationsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid EventId = Guid.CreateVersion7();
    private static readonly PublicIdGenerator Ids = new();

    private static TicketType Ticket(decimal price = 1500m, int groupSize = 1) =>
        TicketType.Create(EventId, "Regular", price, 100, groupSize, null, null, TicketVisibility.Public, 1, Now.AddDays(-10));

    private static Voucher PercentVoucher(decimal percent) =>
        Voucher.Create(EventId, "STUDENT20", VoucherKind.Percent, percent, [], null, null, VoucherSource.Manual, Now.AddDays(-10));

    private static AttendeeDetails Person(string first = "Maria", string email = "maria@example.ph") =>
        new(first, null, "Santos", "Female", email, "+639171234567", null, null, "M", "Facebook");

    private static Registration NewRegistration(TicketType? ticket = null, Voucher? voucher = null)
    {
        var t = ticket ?? Ticket();
        var people = Enumerable.Range(0, t.GroupSize).Select(i => Person($"P{(char)('a' + i)}", $"p{i}@example.ph")).ToList();
        return Registration.Create(EventId, t, people, people.Select(_ => Ids.NewParticipantId()).ToList(), voucher,
            RegistrationSource.Public, null, RegistrationForm.ConsentVersion, Now.AddHours(48), Guid.NewGuid(), Now);
    }

    // ---- Totals ----

    [Theory]
    [InlineData(800, 20, 160, 640)]
    [InlineData(1500, 100, 1500, 0)]
    [InlineData(1000, 33.33, 333.30, 666.70)]
    [InlineData(99.99, 50, 50.00, 49.99)] // 49.995 rounds half away from zero
    public void Percent_voucher_totals(decimal price, decimal percent, decimal discount, decimal total)
    {
        var t = RegistrationTotals.For(price, PercentVoucher(percent));
        Assert.Equal(price, t.Subtotal);
        Assert.Equal(discount, t.Discount);
        Assert.Equal(total, t.Total);
        Assert.Equal(t.Subtotal - t.Discount, t.Total);
    }

    [Fact]
    public void Fixed_voucher_is_capped_at_the_price_and_no_voucher_means_full_price()
    {
        var fixed300 = Voucher.Create(EventId, "SAVE300", VoucherKind.Fixed, 300m, [], null, null, VoucherSource.Manual, Now);
        Assert.Equal(new RegistrationTotals(200m, 200m, 0m), RegistrationTotals.For(200m, fixed300));
        Assert.Equal(new RegistrationTotals(6000m, 300m, 5700m), RegistrationTotals.For(6000m, fixed300));
        Assert.Equal(new RegistrationTotals(0m, 0m, 0m), RegistrationTotals.For(0m, fixed300));
        Assert.Equal(new RegistrationTotals(1500m, 0m, 1500m), RegistrationTotals.For(1500m, null));
    }

    [Fact]
    public void Group_ticket_is_one_unit_priced_per_group()
    {
        var team = Ticket(6000m, groupSize: 5);
        var reg = NewRegistration(team, PercentVoucher(20));
        Assert.Equal(5, reg.Seats);
        Assert.Equal(5, reg.Attendees.Count);
        Assert.Equal(6000m, reg.Subtotal);
        Assert.Equal(1200m, reg.Discount);
        Assert.Equal(4800m, reg.Total);
        Assert.Equal([0, 1, 2, 3, 4], reg.Attendees.Select(a => a.SortOrder));
        Assert.True(reg.Attendees[0].IsBuyer);
        Assert.Equal(5, reg.Attendees.Select(a => a.ParticipantId).Distinct().Count());
    }

    // ---- Creation ----

    [Fact]
    public void Create_checks_group_size_ids_and_consent()
    {
        var team = Ticket(6000m, groupSize: 3);
        AttendeeDetails[] two = [Person(), Person("Jo", "jo@example.ph")];
        Assert.Throws<DomainException>(() => Registration.Create(EventId, team, two, ["HTN-2222-2222", "HTN-3333-3333"], null,
            RegistrationSource.Public, null, RegistrationForm.ConsentVersion, Now.AddHours(48), null, Now));

        var single = Ticket();
        Assert.Throws<DomainException>(() => Registration.Create(EventId, single, [Person()], [], null,
            RegistrationSource.Public, null, RegistrationForm.ConsentVersion, Now, null, Now));
        Assert.Throws<DomainException>(() => Registration.Create(EventId, Ticket(groupSize: 2), [Person(), Person()], ["HTN-2222-2222", "HTN-2222-2222"],
            null, RegistrationSource.Public, null, RegistrationForm.ConsentVersion, Now, null, Now));
        Assert.Throws<DomainException>(() => Registration.Create(EventId, single, [Person()], ["HTN-2222-2222"], null,
            RegistrationSource.Public, null, " ", Now, null, Now));
        Assert.Throws<DomainException>(() => Registration.Create(Guid.NewGuid(), single, [Person()], ["HTN-2222-2222"], null,
            RegistrationSource.Public, null, RegistrationForm.ConsentVersion, Now, null, Now));
    }

    [Fact]
    public void New_registration_is_pending_with_deadline_and_consent()
    {
        var reg = NewRegistration();
        Assert.Equal(RegistrationStatus.PendingPayment, reg.Status);
        Assert.Equal(Now.AddHours(48), reg.Deadline);
        Assert.Equal(Now, reg.ConsentAt);
        Assert.Equal(RegistrationForm.ConsentVersion, reg.ConsentVersion);
        Assert.All(reg.Attendees, a => Assert.Equal(1, a.TicketVersion));
        Assert.True(reg.IsActive);
    }

    [Fact]
    public void Zero_total_has_no_deadline_and_pays_as_comp()
    {
        var reg = NewRegistration(voucher: PercentVoucher(100));
        Assert.Equal(0m, reg.Total);
        Assert.Null(reg.Deadline);
        Assert.False(reg.IsOverdueAt(Now.AddYears(1)));
        var payment = reg.MarkPaid(PaymentMethod.GCash, Guid.NewGuid(), null, null, null, Now);
        Assert.Equal(PaymentMethod.Comp, payment.Method);
        Assert.Equal(0m, payment.Amount);
        Assert.Null(payment.PaymentAccountId);
        Assert.Equal(RegistrationStatus.Paid, reg.Status);
    }

    // ---- Overdue ----

    [Fact]
    public void Overdue_is_pending_past_the_deadline_only()
    {
        var reg = NewRegistration();
        Assert.False(reg.IsOverdueAt(Now.AddHours(48)));
        Assert.True(reg.IsOverdueAt(Now.AddHours(48).AddTicks(1)));
        reg.MarkPaid(PaymentMethod.GCash, null, "ref", null, null, Now.AddHours(50));
        Assert.False(reg.IsOverdueAt(Now.AddDays(10)));
    }

    // ---- Payment, revert, cancel ----

    [Fact]
    public void Mark_paid_records_the_total_and_rejects_bad_transitions()
    {
        var reg = NewRegistration();
        var actor = Guid.NewGuid();
        Assert.Throws<DomainException>(() => reg.MarkPaid(PaymentMethod.Bank, null, null, Now.AddHours(1), actor, Now));
        var p = reg.MarkPaid(PaymentMethod.Bank, null, " BPI 123 ", Now.AddMinutes(-30), actor, Now);
        Assert.Equal(1500m, p.Amount);
        Assert.Equal("BPI 123", p.Reference);
        Assert.Equal(actor, p.MarkedById);
        Assert.Equal(Now, reg.PaidAt);
        Assert.Throws<DomainException>(() => reg.MarkPaid(PaymentMethod.Bank, null, null, null, actor, Now));

        var comp = NewRegistration().MarkPaid(PaymentMethod.Comp, null, null, null, actor, Now);
        Assert.Equal(0m, comp.Amount);
    }

    [Fact]
    public void Revert_payment_bumps_ticket_versions_and_keeps_the_payment()
    {
        var reg = NewRegistration(Ticket(groupSize: 2));
        var p = reg.MarkPaid(PaymentMethod.GCash, null, "ref", null, null, Now);
        Assert.Throws<DomainException>(() => reg.RevertPayment(p, " ", null, Now));
        reg.RevertPayment(p, "Wrong reference", null, Now.AddHours(1));
        Assert.Equal(RegistrationStatus.PendingPayment, reg.Status);
        Assert.Null(reg.PaidAt);
        Assert.True(p.IsReverted);
        Assert.Equal("Wrong reference", p.RevertReason);
        Assert.All(reg.Attendees, a => Assert.Equal(2, a.TicketVersion));
        Assert.Throws<DomainException>(() => reg.RevertPayment(p, "again", null, Now));
    }

    [Fact]
    public void Cancel_from_pending_or_paid_needs_a_reason_and_revokes_tickets()
    {
        var pending = NewRegistration();
        Assert.Throws<DomainException>(() => pending.Cancel("", null, Now));
        pending.Cancel("Asked to cancel", null, Now);
        Assert.Equal(RegistrationStatus.Cancelled, pending.Status);
        Assert.False(pending.IsActive);
        Assert.Equal("Asked to cancel", pending.CancelReason);
        Assert.Throws<DomainException>(() => pending.Cancel("again", null, Now));
        Assert.Throws<DomainException>(() => pending.MarkPaid(PaymentMethod.Cash, null, null, null, null, Now));

        var withLink = NewRegistration();
        withLink.SetProofToken(new string('c', 64), Now);
        withLink.Cancel("Duplicate", null, Now);
        Assert.Null(withLink.ProofTokenHash);

        var paid = NewRegistration();
        paid.MarkPaid(PaymentMethod.Cash, null, null, null, null, Now);
        paid.Cancel("Refunded", null, Now);
        Assert.Equal(2, paid.Attendees[0].TicketVersion);
    }

    [Fact]
    public void Proof_token_only_while_pending()
    {
        var reg = NewRegistration();
        reg.SetProofToken(new string('a', 64), Now);
        Assert.Equal(new string('a', 64), reg.ProofTokenHash);
        Assert.Throws<DomainException>(() => reg.SetProofToken("short", Now));
        reg.MarkPaid(PaymentMethod.Cash, null, null, null, null, Now);
        Assert.Throws<DomainException>(() => reg.SetProofToken(new string('b', 64), Now));
        Assert.Throws<DomainException>(reg.EnsureAcceptsProof);
    }

    // ---- Form helpers ----

    [Theory]
    [InlineData("9171234567", "+639171234567")]
    [InlineData("09171234567", "+639171234567")]
    [InlineData("+639171234567", "+639171234567")]
    [InlineData("639171234567", "+639171234567")]
    [InlineData("+63 917 123 4567", "+639171234567")]
    [InlineData("0917-123-4567", "+639171234567")]
    [InlineData("(0917) 123 4567", "+639171234567")]
    [InlineData("8171234567", null)]
    [InlineData("+19171234567", null)]
    [InlineData("+9171234567", null)]
    [InlineData("917123456", null)]
    [InlineData("91712345678", null)]
    [InlineData("917a234567", null)]
    [InlineData("", null)]
    public void Mobile_numbers_normalize_to_plus_63(string input, string? expected) =>
        Assert.Equal(expected, MobileNumber.Normalize(input));

    // ---- Ticket token ----

    private static readonly byte[] Key = Encoding.UTF8.GetBytes("unit-test-ticket-signing-key-0123456789");

    [Fact]
    public void Token_round_trips()
    {
        var payload = new TicketTokenPayload(Guid.NewGuid(), Guid.NewGuid(), 3);
        var token = TicketTokenCodec.Sign(Key, payload);
        Assert.Matches("^[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]{43}$", token);
        Assert.True(TicketTokenCodec.TryVerify(Key, token, out var read));
        Assert.Equal(payload, read);
    }

    [Fact]
    public void Tampered_or_foreign_tokens_fail()
    {
        var payload = new TicketTokenPayload(Guid.NewGuid(), Guid.NewGuid(), 1);
        var token = TicketTokenCodec.Sign(Key, payload);
        var dot = token.IndexOf('.');

        // Another payload with the original signature.
        var other = TicketTokenCodec.Sign(Key, payload with { TicketVersion = 2 });
        Assert.False(TicketTokenCodec.TryVerify(Key, other[..other.IndexOf('.')] + token[dot..], out _));
        // One flipped character in the signature or in the body.
        Assert.False(TicketTokenCodec.TryVerify(Key, token[..^1] + (token[^1] == 'A' ? 'B' : 'A'), out _));
        Assert.False(TicketTokenCodec.TryVerify(Key, (token[0] == 'A' ? 'B' : 'A') + token[1..], out _));
        // Signed with a different key.
        Assert.False(TicketTokenCodec.TryVerify(Encoding.UTF8.GetBytes("another-ticket-signing-key-0123456789ab"), token, out _));
        // Garbage, empty, extra dots, padding, the short participant ID.
        foreach (var bad in new[] { "", ".", "abc", token + ".x", token + "=", token.Replace('.', ':'), "HTN-7K3Q-9XWD", new string('a', 300) })
            Assert.False(TicketTokenCodec.TryVerify(Key, bad, out _));
        Assert.False(TicketTokenCodec.TryVerify(Key, null, out _));
    }

    [Fact]
    public void Signed_but_malformed_payloads_fail()
    {
        static string SignRaw(string text)
        {
            var body = TicketTokenCodec.Base64Url(Encoding.ASCII.GetBytes(text));
            var mac = System.Security.Cryptography.HMACSHA256.HashData(Key, Encoding.ASCII.GetBytes("htn.ticket.v1." + body));
            return body + "." + TicketTokenCodec.Base64Url(mac);
        }
        var e = Guid.NewGuid();
        var a = Guid.NewGuid();
        Assert.True(TicketTokenCodec.TryVerify(Key, SignRaw($"{e:D}|{a:D}|1"), out _));
        Assert.False(TicketTokenCodec.TryVerify(Key, SignRaw($"{e:D}|{a:D}|0"), out _));
        Assert.False(TicketTokenCodec.TryVerify(Key, SignRaw($"{e:D}|{a:D}|-1"), out _));
        Assert.False(TicketTokenCodec.TryVerify(Key, SignRaw($"{e:D}|{a:D}"), out _));
        Assert.False(TicketTokenCodec.TryVerify(Key, SignRaw($"{e:N}|{a:D}|1"), out _));
        Assert.False(TicketTokenCodec.TryVerify(Key, SignRaw($"{e:D}|{a:D}|1|x"), out _));
    }

    [Fact]
    public void Short_keys_are_refused() =>
        Assert.Throws<ArgumentException>(() => TicketTokenCodec.Sign(new byte[16], new TicketTokenPayload(Guid.NewGuid(), Guid.NewGuid(), 1)));
}
