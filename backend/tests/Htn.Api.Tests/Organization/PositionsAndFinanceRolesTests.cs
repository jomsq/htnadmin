using System.Net;
using System.Text.Json;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Tests.Organization;

public sealed class PositionsAndFinanceRolesTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    private static async Task<string?> CodeAsync(HttpResponseMessage res) =>
        (await ApiClient.JsonAsync(res)).TryGetProperty("code", out var c) ? c.GetString() : null;

    private static async Task<JsonElement> CreatePositionAsync(ApiClient board, string kind, string? title = null)
    {
        var res = await board.PostAsync("/api/positions", new { kind, title = title ?? "Head of " + Guid.NewGuid().ToString("N")[..8] });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return await ApiClient.JsonAsync(res);
    }

    private async Task<List<JsonElement>> SeatsAsync(ApiClient client) =>
        (await ApiClient.JsonAsync(await client.GetAsync("/api/positions"))).EnumerateArray()
        .Where(p => p.GetProperty("kind").GetString() == "BoardSeat").ToList();

    [Fact]
    public async Task Board_seats_are_seeded_and_everyone_with_people_view_can_list()
    {
        var goon = await factory.SignedInAsync(PersonLevel.Goon);
        var res = await goon.GetAsync("/api/positions");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        // Other tests in this class may add seats; the seeded seven sort first.
        var seats = (await SeatsAsync(goon)).Take(7).ToList();
        Assert.Equal(["Chairperson", "Vice Chairperson", "Treasurer", "Corporate Secretary", "Trustee", "Trustee", "Trustee"],
            seats.Select(s => s.GetProperty("title").GetString()!).ToArray());
        Assert.Equal([1, 2, 3, 4, 5, 6, 7], seats.Select(s => s.GetProperty("sortOrder").GetInt32()).ToArray());
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().GetAsync("/api/positions")).StatusCode);
    }

    [Fact]
    public async Task Goon_and_apprentice_cannot_manage_positions()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var position = await CreatePositionAsync(board, "GoonPosition");
        var id = position.GetProperty("id").GetGuid();
        var target = await factory.CreateUserAsync(PersonLevel.Goon);
        foreach (var level in new[] { PersonLevel.Goon, PersonLevel.Apprentice })
        {
            var client = await factory.SignedInAsync(level);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/positions", new { kind = "GoonPosition", title = "Head of Snacks" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsync($"/api/positions/{id}", new { title = "X", isActive = true })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/positions/{id}/assign", new { personId = target.PersonId })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/positions/{id}/vacate")).StatusCode);
        }
    }

    [Fact]
    public async Task Create_update_assign_reassign_vacate_with_history_and_audit()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var created = await CreatePositionAsync(board, "GoonPosition", "Head of Logistics");
        var id = created.GetProperty("id").GetGuid();
        Assert.True(created.GetProperty("isActive").GetBoolean());
        Assert.Equal(JsonValueKind.Null, created.GetProperty("holder").ValueKind);

        var renamed = await ApiClient.JsonAsync(await board.PutAsync($"/api/positions/{id}", new { title = "Head of Logistics & Ops", isActive = true }));
        Assert.Equal("Head of Logistics & Ops", renamed.GetProperty("title").GetString());

        var mika = await factory.CreateUserAsync(PersonLevel.Goon);
        var assigned = await board.PostAsync($"/api/positions/{id}/assign", new { personId = mika.PersonId });
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        var holder = (await ApiClient.JsonAsync(assigned)).GetProperty("holder");
        Assert.Equal(mika.PersonId, holder.GetProperty("id").GetGuid());
        Assert.Equal("Goon", holder.GetProperty("level").GetString());
        Assert.Equal("Head of Logistics & Ops", await factory.DbAsync(db => db.People.Where(p => p.Id == mika.PersonId).Select(p => p.Position).SingleAsync()));

        Assert.Equal("already_holder", await CodeAsync(await board.PostAsync($"/api/positions/{id}/assign", new { personId = mika.PersonId })));
        var held = await board.PutAsync($"/api/positions/{id}", new { title = "Head of Logistics & Ops", isActive = false });
        Assert.Equal(HttpStatusCode.Conflict, held.StatusCode);
        Assert.Equal("position_held", await CodeAsync(held));

        // Change: the old holder's term ends, the new one starts.
        var jo = await factory.CreateUserAsync(PersonLevel.Goon);
        Assert.Equal(HttpStatusCode.OK, (await board.PostAsync($"/api/positions/{id}/assign", new { personId = jo.PersonId })).StatusCode);
        var terms = await factory.DbAsync(db => db.PositionAssignments.AsNoTracking().Where(a => a.PositionId == id).OrderBy(a => a.StartedAt).ToListAsync());
        Assert.Equal(2, terms.Count);
        Assert.Equal(mika.PersonId, terms[0].PersonId);
        Assert.NotNull(terms[0].EndedAt);
        Assert.Equal("Reassigned", terms[0].EndReason);
        Assert.Null(terms[1].EndedAt);
        Assert.True(await factory.DbAsync(db => db.PersonHistory.AnyAsync(h => h.PersonId == mika.PersonId && h.Kind == PersonHistoryKinds.PositionVacated)));
        Assert.True(await factory.DbAsync(db => db.PersonHistory.AnyAsync(h => h.PersonId == jo.PersonId && h.Kind == PersonHistoryKinds.PositionAssigned)));

        var vacated = await board.PostAsync($"/api/positions/{id}/vacate", new { reason = "Term ended" });
        Assert.Equal(HttpStatusCode.OK, vacated.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await ApiClient.JsonAsync(vacated)).GetProperty("holder").ValueKind);
        Assert.Equal("already_vacant", await CodeAsync(await board.PostAsync($"/api/positions/{id}/vacate")));

        var detail = await ApiClient.JsonAsync(await board.GetAsync($"/api/people/{jo.PersonId}"));
        var kinds = detail.GetProperty("history").EnumerateArray().Select(h => h.GetProperty("kind").GetString()).ToList();
        Assert.Equal("PositionVacated", kinds[0]);
        Assert.Contains("PositionAssigned", kinds);
        Assert.Equal("Term ended", detail.GetProperty("history")[0].GetProperty("reason").GetString());

        var actions = await factory.DbAsync(db => db.AuditEntries.AsNoTracking().Where(a => a.EntityId == id.ToString()).Select(a => a.Action).ToListAsync());
        Assert.Contains("position.create", actions);
        Assert.Contains("position.update", actions);
        Assert.Equal(2, actions.Count(a => a == "position.assign"));
        Assert.Contains("position.vacate", actions);

        Assert.Equal(HttpStatusCode.OK, (await board.PutAsync($"/api/positions/{id}", new { title = "Head of Logistics & Ops", isActive = false })).StatusCode);
        Assert.Equal("position_inactive", await CodeAsync(await board.PostAsync($"/api/positions/{id}/assign", new { personId = mika.PersonId })));
    }

    [Fact]
    public async Task Holder_level_and_single_board_seat_rules()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var goonPos = (await CreatePositionAsync(board, "GoonPosition")).GetProperty("id").GetGuid();
        var apprentice = await factory.CreateUserAsync(PersonLevel.Apprentice);
        var res = await board.PostAsync($"/api/positions/{goonPos}/assign", new { personId = apprentice.PersonId });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("wrong_level", await CodeAsync(res));

        var seats = await SeatsAsync(board);
        var goon = await factory.CreateUserAsync(PersonLevel.Goon);
        Assert.Equal("wrong_level", await CodeAsync(await board.PostAsync($"/api/positions/{seats[0].GetProperty("id").GetGuid()}/assign", new { personId = goon.PersonId })));

        // Two new seats so this test doesn't depend on the seeded ones being vacant.
        var seatA = (await CreatePositionAsync(board, "BoardSeat", "Trustee")).GetProperty("id").GetGuid();
        var seatB = (await CreatePositionAsync(board, "BoardSeat", "Trustee")).GetProperty("id").GetGuid();
        var trustee = await factory.CreateUserAsync(PersonLevel.Board);
        Assert.Equal(HttpStatusCode.OK, (await board.PostAsync($"/api/positions/{seatA}/assign", new { personId = trustee.PersonId })).StatusCode);
        var second = await board.PostAsync($"/api/positions/{seatB}/assign", new { personId = trustee.PersonId });
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Equal("already_holds_seat", await CodeAsync(second));

        Assert.Equal(HttpStatusCode.BadRequest, (await board.PostAsync($"/api/positions/{seatB}/assign", new { personId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await board.PostAsync($"/api/positions/{seatB}/assign", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await board.PostAsync($"/api/positions/{Guid.NewGuid()}/assign", new { personId = trustee.PersonId })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await board.PostAsync("/api/positions", new { kind = "Overlord", title = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await board.PostAsync("/api/positions", new { kind = "GoonPosition", title = "" })).StatusCode);
    }

    [Fact]
    public async Task Goon_and_apprentice_cannot_read_or_set_finance_roles()
    {
        var board = await factory.CreateUserAsync(PersonLevel.Board);
        foreach (var level in new[] { PersonLevel.Goon, PersonLevel.Apprentice })
        {
            var client = await factory.SignedInAsync(level);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/finance-roles")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsync("/api/finance-roles", new { cfoPersonId = board.PersonId })).StatusCode);
        }
    }

    [Fact]
    public async Task Cfo_and_assistant_swap_in_one_call_and_are_audited()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var leo = await factory.CreateUserAsync(PersonLevel.Board);
        var ana = await factory.CreateUserAsync(PersonLevel.Board);
        var mika = await factory.CreateUserAsync(PersonLevel.Goon);
        var jo = await factory.CreateUserAsync(PersonLevel.Goon);

        var first = await board.PutAsync("/api/finance-roles", new { cfoPersonId = leo.PersonId, assistantPersonId = mika.PersonId });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var roles = await ApiClient.JsonAsync(first);
        Assert.Equal(leo.PersonId, roles.GetProperty("cfo").GetProperty("id").GetGuid());
        Assert.Equal("Board", roles.GetProperty("cfo").GetProperty("level").GetString());
        Assert.Equal(mika.PersonId, roles.GetProperty("assistant").GetProperty("id").GetGuid());

        var swap = await board.PutAsync("/api/finance-roles", new { cfoPersonId = ana.PersonId, assistantPersonId = jo.PersonId });
        Assert.Equal(HttpStatusCode.OK, swap.StatusCode);

        var flags = await factory.DbAsync(db => db.Users.AsNoTracking().Where(u => u.IsCfo || u.IsFinanceAssistant)
            .Select(u => new { u.Id, u.IsCfo, u.IsFinanceAssistant }).ToListAsync());
        Assert.Equal(ana.UserId, Assert.Single(flags, f => f.IsCfo).Id);
        Assert.Equal(jo.UserId, Assert.Single(flags, f => f.IsFinanceAssistant).Id);

        var get = await ApiClient.JsonAsync(await board.GetAsync("/api/finance-roles"));
        Assert.Equal(ana.PersonId, get.GetProperty("cfo").GetProperty("id").GetGuid());
        Assert.Equal(jo.PersonId, get.GetProperty("assistant").GetProperty("id").GetGuid());

        var audit = (await factory.DbAsync(db => db.AuditEntries.AsNoTracking().Where(a => a.Action == "finance_roles.update").OrderBy(a => a.At).ToListAsync()))[^1];
        var before = JsonDocument.Parse(audit.BeforeJson!).RootElement;
        var after = JsonDocument.Parse(audit.AfterJson!).RootElement;
        Assert.Equal(leo.PersonId, before.GetProperty("cfoPersonId").GetGuid());
        Assert.Equal(mika.PersonId, before.GetProperty("assistantPersonId").GetGuid());
        Assert.Equal(ana.PersonId, after.GetProperty("cfoPersonId").GetGuid());
        Assert.Equal(jo.PersonId, after.GetProperty("assistantPersonId").GetGuid());

        // The new CFO's session picks up the claim.
        var anaClient = factory.NewClient();
        await anaClient.LoginOkAsync(ana.Email, HtnApiFactory.Password);
        var me = await ApiClient.JsonAsync(await anaClient.GetAsync("/api/auth/me"));
        Assert.True(me.GetProperty("isCfo").GetBoolean());
        Assert.Contains("Finance.Post", me.GetProperty("policies").EnumerateArray().Select(p => p.GetString()));

        // Clearing both is allowed.
        var cleared = await ApiClient.JsonAsync(await board.PutAsync("/api/finance-roles", new { cfoPersonId = (Guid?)null, assistantPersonId = (Guid?)null }));
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("cfo").ValueKind);
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("assistant").ValueKind);
    }

    [Fact]
    public async Task Finance_roles_need_the_right_level_and_a_login()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var goon = await factory.CreateUserAsync(PersonLevel.Goon);
        var otherBoard = await factory.CreateUserAsync(PersonLevel.Board);
        var member = await factory.CreateUserAsync(PersonLevel.Member);

        Assert.Equal("wrong_level", await CodeAsync(await board.PutAsync("/api/finance-roles", new { cfoPersonId = goon.PersonId })));
        Assert.Equal("wrong_level", await CodeAsync(await board.PutAsync("/api/finance-roles", new { assistantPersonId = otherBoard.PersonId })));
        Assert.Equal("wrong_level", await CodeAsync(await board.PutAsync("/api/finance-roles", new { assistantPersonId = member.PersonId })));
        Assert.Equal("person_not_found", await CodeAsync(await board.PutAsync("/api/finance-roles", new { cfoPersonId = Guid.NewGuid() })));

        // A Board person without a login (e.g. made Board straight on the roster) can't be CFO.
        var noLogin = await factory.CreateUserAsync(PersonLevel.Member);
        await factory.DbAsync(db => db.People.Where(p => p.Id == noLogin.PersonId).ExecuteUpdateAsync(u => u.SetProperty(p => p.Level, PersonLevel.Board)));
        var res = await board.PutAsync("/api/finance-roles", new { cfoPersonId = noLogin.PersonId });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("no_login", await CodeAsync(res));
    }
}
