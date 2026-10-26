using System.Net;
using System.Text.Json;
using Htn.Api.Tests.CheckIn;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;
using Htn.Infrastructure.Email;
using Htn.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Tests.Organization;

public sealed class PromotionAndDetailTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    private static async Task<string?> CodeAsync(HttpResponseMessage res) =>
        (await ApiClient.JsonAsync(res)).TryGetProperty("code", out var c) ? c.GetString() : null;

    private static async Task<Guid> CreateGoonPositionAsync(ApiClient board, string title)
    {
        var res = await board.PostAsync("/api/positions", new { kind = "GoonPosition", title });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await ApiClient.JsonAsync(res)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Goon_and_apprentice_cannot_promote()
    {
        var member = await factory.CreateUserAsync(PersonLevel.Member);
        foreach (var level in new[] { PersonLevel.Goon, PersonLevel.Apprentice })
        {
            var client = await factory.SignedInAsync(level);
            var res = await client.PostAsync($"/api/people/{member.PersonId}/promote", new { toLevel = "Apprentice" });
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsync($"/api/people/{member.PersonId}",
                new { firstName = "X", lastName = "Y", email = member.Email })).StatusCode);
        }
        Assert.Equal(PersonLevel.Member, await factory.DbAsync(db => db.People.Where(p => p.Id == member.PersonId).Select(p => p.Level).SingleAsync()));
    }

    [Fact]
    public async Task Skipping_a_step_is_400_invalid_promotion()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var member = await factory.CreateUserAsync(PersonLevel.Member);
        var res = await board.PostAsync($"/api/people/{member.PersonId}/promote", new { toLevel = "Goon" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("invalid_promotion", await CodeAsync(res));
        Assert.False(await factory.DbAsync(db => db.Users.AnyAsync(u => u.PersonId == member.PersonId)));
    }

    [Fact]
    public async Task Demotion_without_reason_is_400_on_reason()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var goon = await factory.CreateUserAsync(PersonLevel.Goon);
        var res = await board.PostAsync($"/api/people/{goon.PersonId}/promote", new { toLevel = "Apprentice", reason = " " });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.True((await ApiClient.JsonAsync(res)).GetProperty("errors").TryGetProperty("reason", out _));

        var same = await board.PostAsync($"/api/people/{goon.PersonId}/promote", new { toLevel = "Goon" });
        Assert.Equal(HttpStatusCode.BadRequest, same.StatusCode);
    }

    [Fact]
    public async Task Member_to_apprentice_creates_login_and_queues_invite()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var member = await factory.CreateUserAsync(PersonLevel.Member);

        var res = await board.PostAsync($"/api/people/{member.PersonId}/promote", new { toLevel = "Apprentice" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dto = await ApiClient.JsonAsync(res);
        Assert.Equal("Apprentice", dto.GetProperty("level").GetString());
        Assert.Equal("InvitePending", dto.GetProperty("accountStatus").GetString());

        var user = await factory.DbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.PersonId == member.PersonId));
        Assert.True(await factory.DbAsync(db => db.UserRoles.AnyAsync(r => r.UserId == user.Id && r.RoleId == Roles.ApprenticeId)));
        var outbox = (await factory.DbAsync(db => db.Outbox.AsNoTracking().Where(o => o.Type == EmailTypes.Invite).ToListAsync()))
            .Single(o => o.PayloadJson.Contains(user.Id.ToString(), StringComparison.Ordinal));
        Assert.NotNull(outbox.Secret);

        var audit = await factory.DbAsync(db => db.AuditEntries.AsNoTracking().SingleAsync(a => a.Action == "person.promote" && a.EntityId == member.PersonId.ToString()));
        var after = JsonDocument.Parse(audit.AfterJson!).RootElement;
        Assert.Equal("Apprentice", after.GetProperty("level").GetString());
        Assert.True(after.GetProperty("inviteQueued").GetBoolean());
        Assert.Equal("Member", JsonDocument.Parse(audit.BeforeJson!).RootElement.GetProperty("level").GetString());

        // The invite works and an apprentice doesn't need 2FA set up.
        await factory.ProcessOutboxAsync();
        var token = factory.Emails.LastToken(member.Email, "invite");
        var accept = await factory.NewClient().PostAsync("/api/auth/invite/accept", new { token, password = "promoted-apprentice-1" });
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        Assert.False((await ApiClient.JsonAsync(accept)).GetProperty("requiresTwoFactorSetup").GetBoolean());

        var history = await factory.DbAsync(db => db.PersonHistory.AsNoTracking().Where(h => h.PersonId == member.PersonId).ToListAsync());
        Assert.Contains(history, h => h.Kind == PersonHistoryKinds.LevelChanged && h.FromValue == "Member" && h.ToValue == "Apprentice");
    }

    [Fact]
    public async Task Promotion_to_board_invite_requires_two_factor_setup()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var member = await factory.CreateUserAsync(PersonLevel.Member);
        Assert.Equal(HttpStatusCode.OK, (await board.PostAsync($"/api/people/{member.PersonId}/promote", new { toLevel = "Board" })).StatusCode);
        await factory.ProcessOutboxAsync();
        var token = factory.Emails.LastToken(member.Email, "invite");
        var accept = await factory.NewClient().PostAsync("/api/auth/invite/accept", new { token, password = "new-board-member-1" });
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        Assert.True((await ApiClient.JsonAsync(accept)).GetProperty("requiresTwoFactorSetup").GetBoolean());
    }

    [Fact]
    public async Task Apprentice_to_goon_syncs_the_identity_role_without_a_new_invite()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var apprentice = await factory.CreateUserAsync(PersonLevel.Apprentice);
        var outboxBefore = await factory.DbAsync(db => db.Outbox.CountAsync());

        Assert.Equal(HttpStatusCode.OK, (await board.PostAsync($"/api/people/{apprentice.PersonId}/promote", new { toLevel = "Goon" })).StatusCode);
        var roles = await factory.DbAsync(db => db.UserRoles.Where(r => r.UserId == apprentice.UserId).Select(r => r.RoleId).ToListAsync());
        Assert.Equal([Roles.GoonId], roles);
        Assert.Equal(outboxBefore, await factory.DbAsync(db => db.Outbox.CountAsync()));

        var client = factory.NewClient();
        await client.LoginOkAsync(apprentice.Email, HtnApiFactory.Password);
        var me = await ApiClient.JsonAsync(await client.GetAsync("/api/auth/me"));
        Assert.Equal("Goon", me.GetProperty("level").GetString());
    }

    [Fact]
    public async Task Demoting_a_goon_to_member_vacates_positions_clears_assistant_and_disables_login()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var goon = await factory.CreateUserAsync(PersonLevel.Goon);
        var positionId = await CreateGoonPositionAsync(board, "Head of Demotion " + Guid.NewGuid().ToString("N")[..6]);
        Assert.Equal(HttpStatusCode.OK, (await board.PostAsync($"/api/positions/{positionId}/assign", new { personId = goon.PersonId })).StatusCode);
        var cfo = await factory.CreateUserAsync(PersonLevel.Board);
        Assert.Equal(HttpStatusCode.OK, (await board.PutAsync("/api/finance-roles", new { cfoPersonId = cfo.PersonId, assistantPersonId = goon.PersonId })).StatusCode);

        await factory.SeedCheckInAsync(goon.Email); // Member rule
        var res = await board.PostAsync($"/api/people/{goon.PersonId}/promote", new { toLevel = "Member", reason = "Moved abroad" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dto = await ApiClient.JsonAsync(res);
        Assert.Equal("Member", dto.GetProperty("level").GetString());
        Assert.Equal("NoLogin", dto.GetProperty("accountStatus").GetString());
        Assert.False(dto.GetProperty("isFinanceAssistant").GetBoolean());

        var user = await factory.DbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Id == goon.UserId));
        Assert.False(user.IsActive);
        Assert.False(user.IsFinanceAssistant);
        Assert.False(await factory.DbAsync(db => db.UserRoles.AnyAsync(r => r.UserId == goon.UserId)));
        Assert.Null(await factory.DbAsync(db => db.Positions.Where(p => p.Id == positionId).Select(p => p.HolderId).SingleAsync()));
        Assert.True(await factory.DbAsync(db => db.PersonHistory.AnyAsync(h => h.PersonId == goon.PersonId && h.Kind == PersonHistoryKinds.PositionVacated)));
        var term = await factory.DbAsync(db => db.PositionAssignments.AsNoTracking().SingleAsync(a => a.PositionId == positionId));
        Assert.NotNull(term.EndedAt);

        var audit = await factory.DbAsync(db => db.AuditEntries.AsNoTracking().SingleAsync(a => a.Action == "person.demote" && a.EntityId == goon.PersonId.ToString()));
        Assert.Equal("Moved abroad", audit.Reason);
        Assert.True(JsonDocument.Parse(audit.BeforeJson!).RootElement.GetProperty("isFinanceAssistant").GetBoolean());

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().LoginAsync(goon.Email, HtnApiFactory.Password)).StatusCode);
    }

    [Fact]
    public async Task Board_cannot_change_own_level()
    {
        var self = await factory.CreateUserAsync(PersonLevel.Board);
        var client = factory.NewClient();
        await client.LoginOkAsync(self.Email, HtnApiFactory.Password);
        var res = await client.PostAsync($"/api/people/{self.PersonId}/promote", new { toLevel = "Goon", reason = "x" });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Detail_for_board_has_history_newest_first_with_actor()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var member = await factory.CreateUserAsync(PersonLevel.Member);
        await board.PostAsync($"/api/people/{member.PersonId}/promote", new { toLevel = "Apprentice" });

        var res = await board.GetAsync($"/api/people/{member.PersonId}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var detail = await ApiClient.JsonAsync(res);
        var person = detail.GetProperty("person");
        Assert.Equal(member.Email, person.GetProperty("email").GetString());
        Assert.False(person.GetProperty("isCfo").GetBoolean());
        var history = detail.GetProperty("history").EnumerateArray().ToList();
        Assert.Equal(2, history.Count);
        Assert.Equal("LevelChanged", history[0].GetProperty("kind").GetString());
        Assert.Equal("Member", history[0].GetProperty("from").GetString());
        Assert.Equal("Apprentice", history[0].GetProperty("to").GetString());
        Assert.Equal("Test Board", history[0].GetProperty("actorName").GetString());
        Assert.Equal("Created", history[1].GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Array, detail.GetProperty("positions").ValueKind);

        Assert.Equal(HttpStatusCode.NotFound, (await board.GetAsync($"/api/people/{Guid.NewGuid()}")).StatusCode);
    }

    [Theory]
    [InlineData(PersonLevel.Goon)]
    [InlineData(PersonLevel.Apprentice)]
    public async Task Detail_for_non_board_is_masked_without_history(PersonLevel level)
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var goon = await factory.CreateUserAsync(PersonLevel.Goon, email: $"detail.{Guid.NewGuid():N}@hackthenorth.ph");
        var positionId = await CreateGoonPositionAsync(board, "Head of Detail " + Guid.NewGuid().ToString("N")[..6]);
        await board.PostAsync($"/api/positions/{positionId}/assign", new { personId = goon.PersonId });

        var viewer = await factory.SignedInAsync(level);
        var detail = await ApiClient.JsonAsync(await viewer.GetAsync($"/api/people/{goon.PersonId}"));
        var person = detail.GetProperty("person");
        Assert.Equal("d•••@hackthenorth.ph", person.GetProperty("email").GetString());
        Assert.Equal(JsonValueKind.Null, person.GetProperty("accountStatus").ValueKind);
        Assert.Empty(detail.GetProperty("history").EnumerateArray());
        var position = Assert.Single(detail.GetProperty("positions").EnumerateArray());
        Assert.Equal(positionId, position.GetProperty("id").GetGuid());
        Assert.Equal("GoonPosition", position.GetProperty("kind").GetString());

        // Deactivated people are hidden from non-board callers, like in the list.
        await board.PostAsync($"/api/people/{goon.PersonId}/deactivate");
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/people/{goon.PersonId}")).StatusCode);
        Assert.Null(await factory.DbAsync(db => db.Positions.Where(p => p.Id == positionId).Select(p => p.HolderId).SingleAsync()));
    }

    [Fact]
    public async Task Update_changes_names_and_position_and_audits()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var goon = await factory.CreateUserAsync(PersonLevel.Goon);
        var res = await board.PutAsync($"/api/people/{goon.PersonId}",
            new { firstName = " Mika ", middleName = "Reyes", lastName = "Soriano", email = goon.Email, position = "Head of Logistics" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dto = await ApiClient.JsonAsync(res);
        Assert.Equal("Mika", dto.GetProperty("firstName").GetString());
        Assert.Equal("Head of Logistics", dto.GetProperty("position").GetString());
        Assert.True(await factory.DbAsync(db => db.AuditEntries.AnyAsync(a => a.Action == "person.update" && a.EntityId == goon.PersonId.ToString())));

        var invalid = await board.PutAsync($"/api/people/{goon.PersonId}", new { firstName = "", lastName = "X", email = "nope" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Email_is_locked_once_the_account_is_active()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var goon = await factory.CreateUserAsync(PersonLevel.Goon);
        var res = await board.PutAsync($"/api/people/{goon.PersonId}",
            new { firstName = "Test", lastName = "Goon", email = HtnApiFactory.UniqueEmail("changed") });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("email_locked", await CodeAsync(res));
    }

    [Fact]
    public async Task Email_change_while_invite_pending_moves_the_invite()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var oldEmail = HtnApiFactory.UniqueEmail("typo");
        var id = (await ApiClient.JsonAsync(await board.PostAsync("/api/people",
            new { firstName = "Ana", lastName = "Lim", email = oldEmail, level = "Apprentice" }))).GetProperty("id").GetGuid();
        await factory.ProcessOutboxAsync();
        var oldToken = factory.Emails.LastToken(oldEmail, "invite");

        var newEmail = HtnApiFactory.UniqueEmail("fixed");
        var res = await board.PutAsync($"/api/people/{id}", new { firstName = "Ana", lastName = "Lim", email = newEmail });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(newEmail.ToLowerInvariant(), (await ApiClient.JsonAsync(res)).GetProperty("email").GetString());
        var user = await factory.DbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.PersonId == id));
        Assert.Equal(newEmail.ToLowerInvariant(), user.Email);

        await factory.ProcessOutboxAsync();
        var newToken = factory.Emails.LastToken(newEmail, "invite");
        var anon = factory.NewClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/api/auth/invite/{oldToken}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync($"/api/auth/invite/{newToken}")).StatusCode);

        var other = await factory.CreateUserAsync(PersonLevel.Member);
        var dup = await board.PutAsync($"/api/people/{id}", new { firstName = "Ana", lastName = "Lim", email = other.Email.ToUpperInvariant() });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        Assert.Equal("duplicate_email", await CodeAsync(dup));
    }

    [Fact]
    public async Task List_rows_carry_finance_flags_for_board_only()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var cfo = await factory.CreateUserAsync(PersonLevel.Board);
        var tag = "Flag" + Guid.NewGuid().ToString("N")[..8];
        await factory.DbAsync(db => db.People.Where(p => p.Id == cfo.PersonId).ExecuteUpdateAsync(u => u.SetProperty(p => p.LastName, tag)));
        Assert.Equal(HttpStatusCode.OK, (await board.PutAsync("/api/finance-roles", new { cfoPersonId = cfo.PersonId })).StatusCode);

        var boardItem = (await ApiClient.JsonAsync(await board.GetAsync($"/api/people?search={tag}"))).GetProperty("items")[0];
        Assert.True(boardItem.GetProperty("isCfo").GetBoolean());

        // Non-board callers never learn who holds finance roles (list or detail).
        var goon = await factory.SignedInAsync(PersonLevel.Goon);
        var item = (await ApiClient.JsonAsync(await goon.GetAsync($"/api/people?search={tag}"))).GetProperty("items")[0];
        Assert.False(item.GetProperty("isCfo").GetBoolean());
        Assert.False(item.GetProperty("isFinanceAssistant").GetBoolean());
        var detail = await ApiClient.JsonAsync(await goon.GetAsync($"/api/people/{cfo.PersonId}"));
        Assert.False(detail.GetProperty("person").GetProperty("isCfo").GetBoolean());
    }

    [Fact]
    public async Task Re_enabled_login_gets_a_fresh_invite_and_the_old_password_stops_working()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var goon = await factory.CreateUserAsync(PersonLevel.Goon);
        var stampBefore = await factory.DbAsync(db => db.Users.Where(u => u.Id == goon.UserId).Select(u => u.SecurityStamp).SingleAsync());

        await factory.SeedCheckInAsync(goon.Email); // Member rule
        Assert.Equal(HttpStatusCode.OK, (await board.PostAsync($"/api/people/{goon.PersonId}/promote", new { toLevel = "Member", reason = "Took a break" })).StatusCode);
        var back = await board.PostAsync($"/api/people/{goon.PersonId}/promote", new { toLevel = "Apprentice" });
        Assert.Equal(HttpStatusCode.OK, back.StatusCode);
        Assert.Equal("InvitePending", (await ApiClient.JsonAsync(back)).GetProperty("accountStatus").GetString());

        var user = await factory.DbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Id == goon.UserId));
        Assert.True(user.IsActive);
        Assert.Null(user.PasswordHash);
        Assert.NotEqual(stampBefore, user.SecurityStamp);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().LoginAsync(goon.Email, HtnApiFactory.Password)).StatusCode);

        await factory.ProcessOutboxAsync();
        var token = factory.Emails.LastToken(goon.Email, "invite");
        Assert.Equal(HttpStatusCode.OK, (await factory.NewClient().PostAsync("/api/auth/invite/accept", new { token, password = "back-again-password-1" })).StatusCode);
        await factory.NewClient().LoginOkAsync(goon.Email, "back-again-password-1");
    }

    [Fact]
    public async Task Leaving_the_board_ends_existing_sessions()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var trustee = await factory.CreateUserAsync(PersonLevel.Board);
        var session = factory.NewClient();
        await session.LoginOkAsync(trustee.Email, HtnApiFactory.Password);
        Assert.Equal(HttpStatusCode.OK, (await session.GetAsync("/api/finance-roles")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await board.PostAsync($"/api/people/{trustee.PersonId}/promote", new { toLevel = "Goon", reason = "Term ended" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.GetAsync("/api/finance-roles")).StatusCode);

        // A fresh sign-in works, with Goon rights only.
        var again = factory.NewClient();
        await again.LoginOkAsync(trustee.Email, HtnApiFactory.Password);
        Assert.Equal(HttpStatusCode.Forbidden, (await again.GetAsync("/api/finance-roles")).StatusCode);
    }
}
