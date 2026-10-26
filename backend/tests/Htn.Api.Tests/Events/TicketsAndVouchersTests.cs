using System.Net;
using System.Text.Json;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.Events.EventTestData;

namespace Htn.Api.Tests.Events;

public sealed class TicketsAndVouchersTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task Tickets_are_created_with_defaults_and_listed_in_order()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var id = (await CreateAsync(board)).Id();
        var now = DateTimeOffset.UtcNow;

        var res = await board.PostAsync($"/api/events/{id}/tickets", Ticket("Regular", 1500m, 200));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var regular = await ApiClient.JsonAsync(res);
        Assert.Equal(1500m, regular.GetProperty("price").GetDecimal());
        Assert.Equal(1, regular.GetProperty("groupSize").GetInt32());
        Assert.Equal("Public", regular.GetProperty("visibility").GetString());
        Assert.Equal(0, regular.GetProperty("sold").GetInt32());
        Assert.True(regular.GetProperty("isActive").GetBoolean());
        Assert.Equal("OnSale", regular.GetProperty("status").GetString());
        Assert.Equal(1, regular.GetProperty("sortOrder").GetInt32());

        var team = await AddTicketAsync(board, id, Ticket("Team of 5", 6000m, 10, groupSize: 5, salesStartsAt: now.AddDays(2), salesEndsAt: now.AddDays(20)));
        Assert.Equal("Scheduled", team.GetProperty("status").GetString());
        Assert.Equal(5, team.GetProperty("groupSize").GetInt32());
        Assert.Equal(2, team.GetProperty("sortOrder").GetInt32());

        var comp = await AddTicketAsync(board, id, Ticket("Sponsor comp", 0m, 30, visibility: "Hidden"));
        Assert.Equal("Hidden", comp.GetProperty("status").GetString());
        Assert.Equal(0m, comp.GetProperty("price").GetDecimal());

        var early = await AddTicketAsync(board, id, Ticket("Early bird", 1200m, 150, salesStartsAt: now.AddDays(-30), salesEndsAt: now.AddDays(-1)));
        Assert.Equal("Ended", early.GetProperty("status").GetString());

        var detail = await ApiClient.JsonAsync(await board.GetAsync($"/api/events/{id}"));
        Assert.Equal(["Regular", "Team of 5", "Sponsor comp", "Early bird"],
            detail.GetProperty("tickets").EnumerateArray().Select(t => t.GetProperty("name").GetString()!).ToArray());

        var audit = await factory.DbAsync(db => db.AuditEntries.AsNoTracking()
            .SingleAsync(a => a.Action == "ticket_type.create" && a.EntityId == regular.Id().ToString()));
        Assert.Contains("1500", audit.AfterJson, StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.NotFound, (await board.PostAsync($"/api/events/{Guid.NewGuid()}/tickets", Ticket())).StatusCode);
    }

    [Fact]
    public async Task Ticket_validation()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var id = (await CreateAsync(board)).Id();
        var now = DateTimeOffset.UtcNow;
        (object Body, string Field)[] cases =
        [
            (Ticket(price: -1m), "price"),
            (Ticket(price: 1_000_000.01m), "price"),
            (Ticket(price: 10.005m), "price"),
            (Ticket(quantity: 0), "quantity"),
            (Ticket(quantity: 100_001), "quantity"),
            (Ticket(groupSize: 0), "groupSize"),
            (Ticket(groupSize: 51), "groupSize"),
            (Ticket(name: ""), "name"),
            (Ticket(salesStartsAt: now.AddDays(2), salesEndsAt: now.AddDays(1)), "salesEndsAt"),
            (new { name = "No price", quantity = 10 }, "price"),
        ];
        foreach (var (body, field) in cases)
        {
            var res = await board.PostAsync($"/api/events/{id}/tickets", body);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            Assert.True((await ApiClient.JsonAsync(res)).HasError(field), field);
        }
        Assert.Equal(0, await factory.DbAsync(db => db.TicketTypes.CountAsync(t => t.EventId == id)));
    }

    [Fact]
    public async Task Ticket_update_and_deactivate()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var id = (await CreateAsync(board)).Id();
        var ticket = (await AddTicketAsync(board, id)).Id();

        // Nothing sold yet: the price can change.
        var res = await board.PutAsync($"/api/events/{id}/tickets/{ticket}", Ticket("Regular", 1750.50m, 250, groupSize: 2));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var updated = await ApiClient.JsonAsync(res);
        Assert.Equal(1750.50m, updated.GetProperty("price").GetDecimal());
        Assert.Equal(250, updated.GetProperty("quantity").GetInt32());
        Assert.Equal(2, updated.GetProperty("groupSize").GetInt32());
        var stored = await factory.DbAsync(db => db.TicketTypes.AsNoTracking().SingleAsync(t => t.Id == ticket));
        Assert.Equal(1750.50m, stored.Price);

        var audit = await factory.DbAsync(db => db.AuditEntries.AsNoTracking()
            .SingleAsync(a => a.Action == "ticket_type.update" && a.EntityId == ticket.ToString()));
        Assert.Contains("1500", audit.BeforeJson, StringComparison.Ordinal);
        Assert.Contains("1750.5", audit.AfterJson, StringComparison.Ordinal);

        var off = await board.PostAsync($"/api/events/{id}/tickets/{ticket}/deactivate");
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        var deactivated = await ApiClient.JsonAsync(off);
        Assert.False(deactivated.GetProperty("isActive").GetBoolean());
        Assert.Equal("Inactive", deactivated.GetProperty("status").GetString());

        var again = await board.PostAsync($"/api/events/{id}/tickets/{ticket}/deactivate");
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("ticket_inactive", (await ApiClient.JsonAsync(again)).Code());
        var edit = await board.PutAsync($"/api/events/{id}/tickets/{ticket}", Ticket());
        Assert.Equal("ticket_inactive", (await ApiClient.JsonAsync(edit)).Code());

        // Still listed (never deleted), and audited.
        var detail = await ApiClient.JsonAsync(await board.GetAsync($"/api/events/{id}"));
        Assert.Single(detail.GetProperty("tickets").EnumerateArray());
        Assert.True(await factory.DbAsync(db => db.AuditEntries.AnyAsync(a => a.Action == "ticket_type.deactivate" && a.EntityId == ticket.ToString())));

        // A ticket of another event is not found through this one.
        var other = (await CreateAsync(board)).Id();
        Assert.Equal(HttpStatusCode.NotFound, (await board.PutAsync($"/api/events/{other}/tickets/{ticket}", Ticket())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await board.PostAsync($"/api/events/{other}/tickets/{ticket}/deactivate")).StatusCode);
    }

    [Fact]
    public async Task Vouchers_are_created_upper_case_and_only_managers_see_them()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var id = await OnSaleAsync(board);
        var student = (await AddTicketAsync(board, id, Ticket("Student", 800m, 40))).Id();
        var expires = DateTimeOffset.UtcNow.AddDays(20);

        var res = await board.PostAsync($"/api/events/{id}/vouchers", Voucher(" student20 ", "Percent", 20m, [student], 50, expires));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var v = await ApiClient.JsonAsync(res);
        Assert.Equal("STUDENT20", v.GetProperty("code").GetString());
        Assert.Equal("Percent", v.GetProperty("kind").GetString());
        Assert.Equal(20m, v.GetProperty("value").GetDecimal());
        Assert.Equal([student], v.GetProperty("ticketTypeIds").EnumerateArray().Select(x => x.GetGuid()).ToArray());
        Assert.Equal(50, v.GetProperty("usageLimit").GetInt32());
        Assert.Equal(0, v.GetProperty("used").GetInt32());
        Assert.True(v.GetProperty("isActive").GetBoolean());
        Assert.Equal("Manual", v.GetProperty("source").GetString());

        var fixedRes = await board.PostAsync($"/api/events/{id}/vouchers", Voucher("PARTNER300", "Fixed", 300m, usageLimit: null));
        var fixedV = await ApiClient.JsonAsync(fixedRes);
        Assert.Equal(HttpStatusCode.Created, fixedRes.StatusCode);
        Assert.Equal(300m, fixedV.GetProperty("value").GetDecimal());
        Assert.Equal(0, fixedV.GetProperty("ticketTypeIds").GetArrayLength()); // all tickets
        Assert.Equal(JsonValueKind.Null, fixedV.GetProperty("usageLimit").ValueKind);

        var stored = await factory.DbAsync(db => db.Vouchers.AsNoTracking().SingleAsync(x => x.Id == fixedV.Id()));
        Assert.Equal(300m, stored.AmountOff);
        Assert.Null(stored.PercentOff);

        var boardDetail = await ApiClient.JsonAsync(await board.GetAsync($"/api/events/{id}"));
        Assert.Equal(2, boardDetail.GetProperty("vouchers").GetArrayLength());
        var goon = await factory.SignedInAsync(PersonLevel.Goon);
        Assert.Equal(0, (await ApiClient.JsonAsync(await goon.GetAsync($"/api/events/{id}"))).GetProperty("vouchers").GetArrayLength());

        var audit = await factory.DbAsync(db => db.AuditEntries.AsNoTracking()
            .SingleAsync(a => a.Action == "voucher.create" && a.EntityId == v.Id().ToString()));
        Assert.Contains("STUDENT20", audit.AfterJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Voucher_codes_are_unique_per_event_case_insensitively()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var id = (await CreateAsync(board)).Id();
        Assert.Equal(HttpStatusCode.Created, (await board.PostAsync($"/api/events/{id}/vouchers", Voucher("GOONFAM", value: 100m))).StatusCode);

        var dup = await board.PostAsync($"/api/events/{id}/vouchers", Voucher("goonfam"));
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        Assert.Equal("duplicate_code", (await ApiClient.JsonAsync(dup)).Code());

        // Same code on another event is fine.
        var other = (await CreateAsync(board)).Id();
        Assert.Equal(HttpStatusCode.Created, (await board.PostAsync($"/api/events/{other}/vouchers", Voucher("GOONFAM"))).StatusCode);

        // Renaming into a taken code conflicts; a deactivated voucher still holds its code.
        var second = (await ApiClient.JsonAsync(await board.PostAsync($"/api/events/{id}/vouchers", Voucher("SECOND")))).Id();
        var rename = await board.PutAsync($"/api/events/{id}/vouchers/{second}", Voucher("GoonFam"));
        Assert.Equal("duplicate_code", (await ApiClient.JsonAsync(rename)).Code());

        // The database enforces it too.
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => factory.DbAsync(async db =>
        {
            db.Vouchers.Add(Htn.Domain.Events.Voucher.Create(id, "GOONFAM", Htn.Domain.Events.VoucherKind.Fixed, 1m, [], null, null,
                Htn.Domain.Events.VoucherSource.Manual, DateTimeOffset.UtcNow));
            return await db.SaveChangesAsync();
        }));
        Assert.Contains("ux_vouchers_event_code", ex.InnerException?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Voucher_validation()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var id = (await CreateAsync(board)).Id();
        var otherEvent = (await CreateAsync(board)).Id();
        var foreignTicket = (await AddTicketAsync(board, otherEvent)).Id();

        (object Body, string Field)[] cases =
        [
            (Voucher("AB"), "code"),
            (Voucher("HAS SPACE"), "code"),
            (Voucher("UNDER_SCORE"), "code"),
            (Voucher(new string('A', 31)), "code"),
            (Voucher(kind: "Percent", value: 0m), "value"),
            (Voucher(kind: "Percent", value: 101m), "value"),
            (Voucher(kind: "Percent", value: 12.345m), "value"),
            (Voucher(kind: "Fixed", value: 0m), "value"),
            (Voucher(kind: "Fixed", value: -300m), "value"),
            (Voucher(usageLimit: 0), "usageLimit"),
            (new { code = "NOKIND", value = 10m }, "kind"),
            (new { code = "NOVALUE", kind = "Fixed" }, "value"),
        ];
        foreach (var (body, field) in cases)
        {
            var res = await board.PostAsync($"/api/events/{id}/vouchers", body);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            Assert.True((await ApiClient.JsonAsync(res)).HasError(field), field);
        }

        var foreign = await board.PostAsync($"/api/events/{id}/vouchers", Voucher(ticketTypeIds: [foreignTicket]));
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
        Assert.Equal("unknown_ticket_type", (await ApiClient.JsonAsync(foreign)).Code());

        Assert.Equal(0, await factory.DbAsync(db => db.Vouchers.CountAsync(v => v.EventId == id)));
        Assert.Equal(HttpStatusCode.NotFound, (await board.PostAsync($"/api/events/{Guid.NewGuid()}/vouchers", Voucher())).StatusCode);
    }

    [Fact]
    public async Task Voucher_update_and_deactivate()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var id = (await CreateAsync(board)).Id();
        var ticket = (await AddTicketAsync(board, id)).Id();
        var voucher = (await ApiClient.JsonAsync(await board.PostAsync($"/api/events/{id}/vouchers", Voucher("EARLY", "Percent", 10m)))).Id();

        var res = await board.PutAsync($"/api/events/{id}/vouchers/{voucher}", Voucher("early-300", "Fixed", 300m, [ticket], 5));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var v = await ApiClient.JsonAsync(res);
        Assert.Equal("EARLY-300", v.GetProperty("code").GetString());
        Assert.Equal("Fixed", v.GetProperty("kind").GetString());
        Assert.Equal(300m, v.GetProperty("value").GetDecimal());
        Assert.Equal(5, v.GetProperty("usageLimit").GetInt32());
        var stored = await factory.DbAsync(db => db.Vouchers.AsNoTracking().SingleAsync(x => x.Id == voucher));
        Assert.Null(stored.PercentOff);
        Assert.Equal([ticket], stored.TicketTypeIds);

        var audit = await factory.DbAsync(db => db.AuditEntries.AsNoTracking()
            .SingleAsync(a => a.Action == "voucher.update" && a.EntityId == voucher.ToString()));
        Assert.Contains("EARLY", audit.BeforeJson, StringComparison.Ordinal);
        Assert.Contains("EARLY-300", audit.AfterJson, StringComparison.Ordinal);

        var off = await board.PostAsync($"/api/events/{id}/vouchers/{voucher}/deactivate");
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        Assert.False((await ApiClient.JsonAsync(off)).GetProperty("isActive").GetBoolean());
        Assert.Equal("voucher_inactive", (await ApiClient.JsonAsync(await board.PostAsync($"/api/events/{id}/vouchers/{voucher}/deactivate"))).Code());
        Assert.Equal("voucher_inactive", (await ApiClient.JsonAsync(await board.PutAsync($"/api/events/{id}/vouchers/{voucher}", Voucher("EARLY")))).Code());
        Assert.True(await factory.DbAsync(db => db.AuditEntries.AnyAsync(a => a.Action == "voucher.deactivate" && a.EntityId == voucher.ToString())));

        var other = (await CreateAsync(board)).Id();
        Assert.Equal(HttpStatusCode.NotFound, (await board.PutAsync($"/api/events/{other}/vouchers/{voucher}", Voucher())).StatusCode);
    }

    [Fact]
    public async Task Events_tickets_and_vouchers_are_never_deleted()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var id = (await CreateAsync(board)).Id();
        await AddTicketAsync(board, id);
        await board.PostAsync($"/api/events/{id}/vouchers", Voucher("KEEPME"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => factory.DbAsync(async db =>
        {
            db.Events.Remove(await db.Events.SingleAsync(e => e.Id == id));
            return await db.SaveChangesAsync();
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => factory.DbAsync(async db =>
        {
            db.TicketTypes.Remove(await db.TicketTypes.FirstAsync(t => t.EventId == id));
            return await db.SaveChangesAsync();
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => factory.DbAsync(async db =>
        {
            db.Vouchers.Remove(await db.Vouchers.FirstAsync(v => v.EventId == id));
            return await db.SaveChangesAsync();
        }));
    }
}
