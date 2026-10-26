using System.Net;
using System.Text.Json;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Tests.Settings;

public sealed class PaymentAccountsEndpointsTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    private const string Url = "/api/settings/payment-accounts";

    private static object GCash(string number = "0917 123 4567", bool isDefault = false, string? label = null) => new
    {
        kind = "GCash",
        label = label ?? "GCash — HTN " + Guid.NewGuid().ToString("N")[..6],
        accountName = "HackTheNorth.PH Inc.",
        accountNumber = number,
        isDefault,
    };

    private static async Task<JsonElement> CreateAsync(ApiClient board, object body)
    {
        var res = await board.PostAsync(Url, body);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return await ApiClient.JsonAsync(res);
    }

    [Fact]
    public async Task Non_board_cannot_reach_payment_accounts_at_all()
    {
        // Settings.Manage is Board only, so non-board callers never see an account number (masked or full).
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var id = (await CreateAsync(board, GCash())).GetProperty("id").GetGuid();
        foreach (var level in new[] { PersonLevel.Goon, PersonLevel.Apprentice })
        {
            var client = await factory.SignedInAsync(level);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(Url, GCash())).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsync($"{Url}/{id}", GCash())).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"{Url}/{id}/deactivate", new { reason = "x" })).StatusCode);
        }
    }

    [Fact]
    public async Task Board_sees_full_and_masked_numbers_and_audit_is_masked()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var created = await CreateAsync(board, GCash("0917 123 4567"));
        Assert.Equal("0917 123 4567", created.GetProperty("accountNumber").GetString());
        Assert.Equal("•••• 4567", created.GetProperty("accountNumberMasked").GetString());
        Assert.Equal(JsonValueKind.Null, created.GetProperty("bankName").ValueKind);
        var id = created.GetProperty("id").GetGuid();

        var list = await ApiClient.JsonAsync(await board.GetAsync(Url));
        var row = list.EnumerateArray().Single(a => a.GetProperty("id").GetGuid() == id);
        Assert.Equal("0917 123 4567", row.GetProperty("accountNumber").GetString());

        var updated = await board.PutAsync($"{Url}/{id}", new
        {
            kind = "Bank", label = "BPI — HTN", bankName = "BPI", accountName = "HackTheNorth.PH Inc.", accountNumber = "1234-5678-90", isDefault = false,
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("•••• 7890", (await ApiClient.JsonAsync(updated)).GetProperty("accountNumberMasked").GetString());

        var audits = await factory.DbAsync(db => db.AuditEntries.AsNoTracking().Where(a => a.EntityId == id.ToString()).ToListAsync());
        Assert.Contains(audits, a => a.Action == "payment_account.create");
        Assert.Contains(audits, a => a.Action == "payment_account.update");
        foreach (var a in audits)
        {
            var text = (a.BeforeJson ?? "") + (a.AfterJson ?? "");
            Assert.DoesNotContain("0917 123 4567", text, StringComparison.Ordinal);
            Assert.DoesNotContain("1234-5678-90", text, StringComparison.Ordinal);
        }
        Assert.Contains("4567", audits.Single(a => a.Action == "payment_account.create").AfterJson!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Setting_a_default_clears_the_previous_one()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var first = (await CreateAsync(board, GCash(isDefault: true))).GetProperty("id").GetGuid();
        var second = await CreateAsync(board, new { kind = "Cash", label = "Cash at the desk", accountName = "Registration desk", isDefault = true });
        Assert.True(second.GetProperty("isDefault").GetBoolean());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("accountNumber").ValueKind);

        var defaults = await factory.DbAsync(db => db.PaymentAccounts.Where(a => a.IsDefault).Select(a => a.Id).ToListAsync());
        Assert.Equal([second.GetProperty("id").GetGuid()], defaults);

        var list = (await ApiClient.JsonAsync(await board.GetAsync(Url))).EnumerateArray().ToList();
        Assert.True(list[0].GetProperty("isDefault").GetBoolean()); // default first
        Assert.False(list.Single(a => a.GetProperty("id").GetGuid() == first).GetProperty("isDefault").GetBoolean());

        var back = await board.PutAsync($"{Url}/{first}", GCash(isDefault: true, label: "GCash main"));
        Assert.True((await ApiClient.JsonAsync(back)).GetProperty("isDefault").GetBoolean());
        Assert.Equal([first], await factory.DbAsync(db => db.PaymentAccounts.Where(a => a.IsDefault).Select(a => a.Id).ToListAsync()));
    }

    [Fact]
    public async Task Deactivate_needs_a_reason_keeps_the_row_and_hides_it_by_default()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var id = (await CreateAsync(board, GCash(isDefault: true))).GetProperty("id").GetGuid();

        var noReason = await board.PostAsync($"{Url}/{id}/deactivate", new { reason = "" });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.True((await ApiClient.JsonAsync(noReason)).GetProperty("errors").TryGetProperty("reason", out _));

        Assert.Equal(HttpStatusCode.NoContent, (await board.PostAsync($"{Url}/{id}/deactivate", new { reason = "Number changed" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await board.PostAsync($"{Url}/{id}/deactivate", new { reason = "again" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await board.PutAsync($"{Url}/{id}", GCash())).StatusCode);

        var active = await ApiClient.JsonAsync(await board.GetAsync(Url));
        Assert.DoesNotContain(active.EnumerateArray(), a => a.GetProperty("id").GetGuid() == id);
        var all = await ApiClient.JsonAsync(await board.GetAsync($"{Url}?includeInactive=true"));
        var row = all.EnumerateArray().Single(a => a.GetProperty("id").GetGuid() == id);
        Assert.False(row.GetProperty("isActive").GetBoolean());
        Assert.False(row.GetProperty("isDefault").GetBoolean());

        var audit = await factory.DbAsync(db => db.AuditEntries.AsNoTracking().SingleAsync(a => a.Action == "payment_account.deactivate" && a.EntityId == id.ToString()));
        Assert.Equal("Number changed", audit.Reason);
        Assert.Equal(HttpStatusCode.NotFound, (await board.PostAsync($"{Url}/{Guid.NewGuid()}/deactivate", new { reason = "x" })).StatusCode);
    }

    [Fact]
    public async Task Validation_by_kind()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var bank = await board.PostAsync(Url, new { kind = "Bank", label = "BDO", accountName = "HTN", accountNumber = "123456" });
        Assert.Equal(HttpStatusCode.BadRequest, bank.StatusCode);
        Assert.True((await ApiClient.JsonAsync(bank)).GetProperty("errors").TryGetProperty("bankName", out _));

        var gcash = await board.PostAsync(Url, new { kind = "GCash", label = "GCash", accountName = "HTN" });
        Assert.True((await ApiClient.JsonAsync(gcash)).GetProperty("errors").TryGetProperty("accountNumber", out _));

        var formula = await board.PostAsync(Url, GCash("=HYPERLINK(1)"));
        Assert.True((await ApiClient.JsonAsync(formula)).GetProperty("errors").TryGetProperty("accountNumber", out _));

        var noKind = await board.PostAsync(Url, new { label = "x", accountName = "y", accountNumber = "1234" });
        Assert.True((await ApiClient.JsonAsync(noKind)).GetProperty("errors").TryGetProperty("kind", out _));
    }
}
