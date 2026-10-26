using System.Net;
using System.Text;
using System.Text.Json;
using Htn.Api.Features.Registrations;
using Htn.Api.Seeding;
using Htn.Api.Tests.Hosting;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Registrations;
using Htn.Infrastructure.Email;
using Htn.Infrastructure.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Htn.Api.Tests.Registrations;

public sealed class RegistrationCsvTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("Maria", "Maria")]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+639171234567", "'+639171234567")]
    [InlineData("-5", "'-5")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\tx", "'\tx")]
    [InlineData("\rx", "\"'\rx\"")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("line\nbreak", "\"line\nbreak\"")]
    [InlineData("a=b", "a=b")]
    public void Cells_are_quoted_and_formula_safe(string? input, string expected) =>
        Assert.Equal(expected, RegistrationCsv.Cell(input));
}

public sealed class IcsCalendarTests
{
    [Fact]
    public void Text_is_escaped_per_rfc_5545()
    {
        Assert.Equal(@"a\\b\;c\,d\ne\nf", IcsCalendar.Escape("a\\b;c,d\r\ne\nf"));
        Assert.Equal("nocontrol", IcsCalendar.Escape("no\u0007control"));
    }

    [Fact]
    public void Long_lines_fold_at_75_octets_without_splitting_characters()
    {
        var line = "DESCRIPTION:" + string.Concat(Enumerable.Repeat("ñ", 80));
        var folded = IcsCalendar.Fold(line);
        var parts = folded.Split("\r\n");
        Assert.True(parts.Length > 1);
        Assert.All(parts, p => Assert.True(Encoding.UTF8.GetByteCount(p) <= 75, p));
        Assert.All(parts.Skip(1), p => Assert.StartsWith(" ", p, StringComparison.Ordinal));
        Assert.Equal(line, string.Concat(parts.Select((p, i) => i == 0 ? p : p[1..])));
    }

    [Fact]
    public void Event_has_utc_times_and_crlf_lines()
    {
        var ics = IcsCalendar.Event("abc@hackthenorth.ph", new DateTimeOffset(2026, 11, 22, 8, 0, 0, TimeSpan.FromHours(8)),
            new DateTimeOffset(2026, 11, 22, 18, 0, 0, TimeSpan.FromHours(8)), "HTN Con 2026", "SMX, Pasay", "Bring; your ID",
            new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero));
        Assert.Contains("\r\nDTSTART:20261122T000000Z\r\n", ics, StringComparison.Ordinal);
        Assert.Contains("\r\nDTEND:20261122T100000Z\r\n", ics, StringComparison.Ordinal);
        Assert.Contains("\r\nLOCATION:SMX\\, Pasay\r\n", ics, StringComparison.Ordinal);
        Assert.Contains("\r\nDESCRIPTION:Bring\\; your ID\r\n", ics, StringComparison.Ordinal);
        Assert.EndsWith("END:VCALENDAR\r\n", ics, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", ics.Replace("\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }
}

public sealed class TicketSigningTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    [Fact]
    public void Key_is_required_outside_development_and_must_be_long_enough()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => TicketSigning.EnsureConfigured(Config(), new FakeEnvironment("Production")));
        Assert.Contains("TICKET_SIGNING_KEY", ex.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => TicketSigning.EnsureConfigured(Config(), new FakeEnvironment("Staging")));
        Assert.Throws<InvalidOperationException>(() =>
            TicketSigning.EnsureConfigured(Config(("TICKET_SIGNING_KEY", "too-short")), new FakeEnvironment("Development")));

        TicketSigning.EnsureConfigured(Config(), new FakeEnvironment("Development"));
        TicketSigning.EnsureConfigured(Config(("TICKET_SIGNING_KEY", new string('k', 32))), new FakeEnvironment("Production"));
        TicketSigning.EnsureConfigured(Config((TicketSigning.ConfigKey, new string('k', 40))), new FakeEnvironment("Production"));
    }

    [Fact]
    public void Service_signs_with_the_configured_key_only()
    {
        var a = new TicketToken(Config((TicketSigning.ConfigKey, new string('a', 40))), new FakeEnvironment("Production"));
        var b = new TicketToken(Config((TicketSigning.ConfigKey, new string('b', 40))), new FakeEnvironment("Production"));
        var token = a.Issue(Guid.NewGuid(), Guid.NewGuid(), 1);
        Assert.True(a.TryVerify(token, out _));
        Assert.False(b.TryVerify(token, out _));
    }
}

public sealed class MailtrapAttachmentTests
{
    private sealed class Capture : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"success":true,"message_ids":["m-1"]}""", Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public async Task Attachments_are_base64_with_inline_disposition_and_content_id()
    {
        var capture = new Capture();
        var sender = new MailtrapEmailSender(new HttpClient(capture),
            Options.Create(new MailtrapOptions { ApiToken = "token", Mode = "live", ReplyTo = "Team <team@example.ph>" }), NullLogger<MailtrapEmailSender>.Instance);
        var result = await sender.SendAsync(new EmailMessage("a@example.ph", "A", "Subject", "<img src=\"cid:ticket-qr\">", "text", "ticket",
            [new EmailAttachment("ticket-qr.png", "image/png", [1, 2, 3], "ticket-qr"), new EmailAttachment("event.ics", "text/calendar", "BEGIN"u8.ToArray())]),
            CancellationToken.None);
        Assert.Equal("m-1", result.ExternalId);

        var json = JsonDocument.Parse(capture.Body!).RootElement;
        var attachments = json.GetProperty("attachments");
        Assert.Equal(2, attachments.GetArrayLength());
        Assert.Equal("AQID", attachments[0].GetProperty("content").GetString());
        Assert.Equal("ticket-qr.png", attachments[0].GetProperty("filename").GetString());
        Assert.Equal("image/png", attachments[0].GetProperty("type").GetString());
        Assert.Equal("inline", attachments[0].GetProperty("disposition").GetString());
        Assert.Equal("ticket-qr", attachments[0].GetProperty("content_id").GetString());
        Assert.Equal("attachment", attachments[1].GetProperty("disposition").GetString());
        Assert.False(attachments[1].TryGetProperty("content_id", out _));
        Assert.Equal("team@example.ph", json.GetProperty("reply_to").GetProperty("email").GetString());

        // No attachments: the field is left out.
        await sender.SendAsync(new EmailMessage("a@example.ph", null, "S", "<p>h</p>", "t", "invite"), CancellationToken.None);
        Assert.False(JsonDocument.Parse(capture.Body!).RootElement.TryGetProperty("attachments", out _));
    }
}

public sealed class DevSeedRegistrationsTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task Dev_seed_adds_sample_registrations_across_statuses_once()
    {
        await DevSeeder.SeedAsync(factory.Services);
        await DevSeeder.SeedAsync(factory.Services);
        var regs = await factory.DbAsync(db => db.Registrations.AsNoTracking().ToListAsync());
        Assert.Contains(regs, r => r.Status == RegistrationStatus.PendingPayment && r.IsOverdueAt(DateTimeOffset.UtcNow));
        Assert.Contains(regs, r => r.Status == RegistrationStatus.PendingPayment && !r.IsOverdueAt(DateTimeOffset.UtcNow));
        Assert.Contains(regs, r => r.Status == RegistrationStatus.Paid && r.VoucherCode == "STUDENT20");
        Assert.Contains(regs, r => r.Status == RegistrationStatus.Paid && r.Source == RegistrationSource.Manual && r.Total == 0);
        Assert.Contains(regs, r => r.Status == RegistrationStatus.Cancelled);
        Assert.Contains(regs, r => r.Seats == 5);
        Assert.Equal(7, regs.Count);
        Assert.Empty(await factory.DbAsync(db => db.Outbox.AsNoTracking().Where(o => o.Type.StartsWith("email.")).ToListAsync()));

        // Seeded Members predate the check-in rule: created through the exempt path, and history says so.
        var paolo = await factory.DbAsync(db => db.People.AsNoTracking().SingleAsync(p => p.Email == "paolo@hackthenorth.ph"));
        Assert.Equal(Htn.Domain.Organization.PersonLevel.Member, paolo.Level);
        Assert.Equal(Htn.Domain.Organization.MemberRule.SeededReason, await factory.DbAsync(db => db.PersonHistory.AsNoTracking()
            .Where(h => h.PersonId == paolo.Id && h.Kind == Htn.Domain.Organization.PersonHistoryKinds.Created).Select(h => h.Reason).SingleAsync()));
    }
}

/// <summary>Development host: DI is validated on build, and no ticket key falls back to the dev-only key.</summary>
public sealed class DevelopmentApiFactory : HtnApiFactory
{
    protected override string Environment => "Development";

    protected override IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>
    {
        ["Seed:Dev"] = "false",
        [TicketSigning.ConfigKey] = "",
    };
}

public sealed class DevelopmentStartupTests(DevelopmentApiFactory factory) : IClassFixture<DevelopmentApiFactory>
{
    [Fact]
    public async Task Development_host_starts_and_signs_with_the_dev_key()
    {
        var res = await factory.NewClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var tokens = factory.Services.GetRequiredService<ITicketToken>();
        var token = tokens.Issue(Guid.NewGuid(), Guid.NewGuid(), 1);
        Assert.True(TicketTokenCodec.TryVerify(Encoding.UTF8.GetBytes(TicketSigning.DevelopmentKey), token, out _));
    }
}
