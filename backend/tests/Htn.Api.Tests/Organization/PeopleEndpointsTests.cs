using System.Net;
using System.Text.Json;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Outbox;
using Htn.Domain.Organization;
using Htn.Infrastructure.Email;
using Htn.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Tests.Organization;

public sealed class PeopleEndpointsTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    private static object NewPerson(string level, string? email = null) => new
    {
        firstName = "Juan",
        middleName = "Santos",
        lastName = "Dela Cruz",
        email = email ?? HtnApiFactory.UniqueEmail("juan"),
        level,
        position = "Head of Programs",
    };

    [Fact]
    public async Task Anonymous_list_is_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().GetAsync("/api/people")).StatusCode);
    }

    [Fact]
    public async Task Goon_cannot_create_people()
    {
        var goon = await factory.SignedInAsync(PersonLevel.Goon);
        var res = await goon.PostAsync("/api/people", NewPerson("Member"));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Apprentice_cannot_manage_accounts()
    {
        var target = await factory.CreateUserAsync(PersonLevel.Goon);
        var apprentice = await factory.SignedInAsync(PersonLevel.Apprentice);
        Assert.Equal(HttpStatusCode.Forbidden, (await apprentice.PostAsync($"/api/people/{target.PersonId}/deactivate")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await apprentice.PostAsync($"/api/people/{target.PersonId}/resend-invite")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await apprentice.PostAsync($"/api/people/{target.PersonId}/send-password-reset")).StatusCode);
    }

    [Fact]
    public async Task Goon_and_apprentice_can_view_people()
    {
        Assert.Equal(HttpStatusCode.OK, (await (await factory.SignedInAsync(PersonLevel.Goon)).GetAsync("/api/people")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await factory.SignedInAsync(PersonLevel.Apprentice)).GetAsync("/api/people")).StatusCode);
    }

    [Fact]
    public async Task Create_goon_creates_user_invite_outbox_and_audit()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var email = HtnApiFactory.UniqueEmail("NewGoon");
        var res = await board.PostAsync("/api/people", NewPerson("Goon", email));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var dto = await ApiClient.JsonAsync(res);
        var id = dto.GetProperty("id").GetGuid();
        Assert.Equal($"/api/people/{id}", res.Headers.Location?.OriginalString);
        Assert.Equal(email.ToLowerInvariant(), dto.GetProperty("email").GetString());
        Assert.Equal("Goon", dto.GetProperty("level").GetString());
        Assert.Equal("Santos", dto.GetProperty("middleName").GetString());
        Assert.Equal("Head of Programs", dto.GetProperty("position").GetString());
        Assert.True(dto.GetProperty("isActive").GetBoolean());
        Assert.Equal("InvitePending", dto.GetProperty("accountStatus").GetString());
        Assert.Equal(JsonValueKind.Null, dto.GetProperty("lastSignInAt").ValueKind);

        var user = await factory.DbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.PersonId == id));
        Assert.Null(user.PasswordHash);
        Assert.True(await factory.DbAsync(db => db.UserRoles.AnyAsync(r => r.UserId == user.Id && r.RoleId == Roles.GoonId)));

        var token = await factory.DbAsync(db => db.AccountTokens.AsNoTracking().SingleAsync(t => t.UserId == user.Id));
        Assert.Equal(AccountTokenPurpose.Invite, token.Purpose);
        Assert.Equal(TimeSpan.FromHours(48), token.ExpiresAt - token.CreatedAt);
        Assert.Equal(64, token.TokenHash.Length);

        var outbox = (await factory.DbAsync(db => db.Outbox.AsNoTracking().Where(o => o.Type == EmailTypes.Invite).ToListAsync()))
            .Single(o => o.PayloadJson.Contains(user.Id.ToString(), StringComparison.Ordinal));
        Assert.Equal(OutboxStatus.Pending, outbox.Status);
        Assert.NotNull(outbox.Secret);
        Assert.DoesNotContain(outbox.Secret!, outbox.PayloadJson, StringComparison.Ordinal);
        Assert.Equal(AccountTokenService.Hash(outbox.Secret!), token.TokenHash.Trim());

        var audit = await factory.DbAsync(db => db.AuditEntries.AsNoTracking().SingleAsync(a => a.Action == "person.create" && a.EntityId == id.ToString()));
        Assert.NotNull(audit.ActorId);
        Assert.True(JsonDocument.Parse(audit.AfterJson!).RootElement.GetProperty("hasLogin").GetBoolean());
        Assert.DoesNotContain(outbox.Secret!, audit.AfterJson!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_member_is_roster_only()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var email = HtnApiFactory.UniqueEmail("member");
        var res = await board.PostAsync("/api/people", NewPerson("Member", email));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var dto = await ApiClient.JsonAsync(res);
        Assert.Equal("NoLogin", dto.GetProperty("accountStatus").GetString());

        Assert.False(await factory.DbAsync(db => db.Users.AnyAsync(u => u.Email == email)));
        Assert.DoesNotContain(await factory.DbAsync(db => db.Outbox.AsNoTracking().ToListAsync()), o => o.PayloadJson.Contains(email, StringComparison.Ordinal));
        Assert.True(await factory.DbAsync(db => db.AuditEntries.AnyAsync(a => a.Action == "person.create" && a.EntityId == dto.GetProperty("id").GetString())));
    }

    [Fact]
    public async Task Duplicate_email_is_rejected_case_insensitively()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var email = HtnApiFactory.UniqueEmail("dup");
        Assert.Equal(HttpStatusCode.Created, (await board.PostAsync("/api/people", NewPerson("Member", email))).StatusCode);

        var res = await board.PostAsync("/api/people", NewPerson("Goon", email.ToUpperInvariant()));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var errors = (await ApiClient.JsonAsync(res)).GetProperty("errors");
        Assert.Contains("already in the organization", errors.GetProperty("email")[0].GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_create_is_400_with_field_errors()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var res = await board.PostAsync("/api/people", new { firstName = "", lastName = "X", email = "not-an-email", level = "Goon" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var errors = (await ApiClient.JsonAsync(res)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("firstName", out _));
        Assert.True(errors.TryGetProperty("email", out _));

        var noLevel = await board.PostAsync("/api/people", new { firstName = "A", lastName = "X", email = HtnApiFactory.UniqueEmail() });
        Assert.Equal(HttpStatusCode.BadRequest, noLevel.StatusCode);
    }

    [Fact]
    public async Task List_filters_searches_and_pages()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var tag = Guid.NewGuid().ToString("N")[..8];
        for (var i = 0; i < 3; i++)
            await board.PostAsync("/api/people", new { firstName = $"Zed{tag}", lastName = $"Apprentice{i}", email = HtnApiFactory.UniqueEmail(), level = "Apprentice" });
        await board.PostAsync("/api/people", new { firstName = $"Zed{tag}", lastName = "Member", email = HtnApiFactory.UniqueEmail(), level = "Member" });

        var all = await ApiClient.JsonAsync(await board.GetAsync($"/api/people?search=zed{tag}"));
        Assert.Equal(4, all.GetProperty("total").GetInt32());

        var paged = await ApiClient.JsonAsync(await board.GetAsync($"/api/people?search=zed{tag}&level=apprentice&page=2&pageSize=2"));
        Assert.Equal(3, paged.GetProperty("total").GetInt32());
        Assert.Equal(2, paged.GetProperty("page").GetInt32());
        Assert.Equal(2, paged.GetProperty("pageSize").GetInt32());
        var item = Assert.Single(paged.GetProperty("items").EnumerateArray());
        Assert.Equal("Apprentice2", item.GetProperty("lastName").GetString());

        var wildcard = await ApiClient.JsonAsync(await board.GetAsync("/api/people?search=%25%25%25" + tag));
        Assert.Equal(0, wildcard.GetProperty("total").GetInt32());

        Assert.Equal(HttpStatusCode.BadRequest, (await board.GetAsync("/api/people?level=Overlord")).StatusCode);
        var clamped = await ApiClient.JsonAsync(await board.GetAsync("/api/people?pageSize=1000"));
        Assert.Equal(100, clamped.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task Resend_invite_replaces_the_old_link()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var email = HtnApiFactory.UniqueEmail("resend");
        var id = (await ApiClient.JsonAsync(await board.PostAsync("/api/people", NewPerson("Apprentice", email)))).GetProperty("id").GetGuid();
        await factory.ProcessOutboxAsync();
        var first = factory.Emails.LastToken(email, "invite");

        Assert.Equal(HttpStatusCode.NoContent, (await board.PostAsync($"/api/people/{id}/resend-invite")).StatusCode);
        await factory.ProcessOutboxAsync();
        var second = factory.Emails.LastToken(email, "invite");
        Assert.NotEqual(first, second);

        var anon = factory.NewClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/api/auth/invite/{first}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.PostAsync("/api/auth/invite/accept", new { token = first, password = "long-enough-password" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await anon.PostAsync("/api/auth/invite/accept", new { token = second, password = "long-enough-password" })).StatusCode);
        Assert.True(await factory.DbAsync(db => db.AuditEntries.AnyAsync(a => a.Action == "person.invite_resent" && a.EntityId == id.ToString())));

        // Already active now.
        Assert.Equal(HttpStatusCode.Conflict, (await board.PostAsync($"/api/people/{id}/resend-invite")).StatusCode);
    }

    [Fact]
    public async Task Invite_info_shows_inviter_and_level()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var email = HtnApiFactory.UniqueEmail("info");
        await board.PostAsync("/api/people", NewPerson("Goon", email));
        await factory.ProcessOutboxAsync();
        var token = factory.Emails.LastToken(email, "invite");

        var res = await factory.NewClient().GetAsync($"/api/auth/invite/{token}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var info = await ApiClient.JsonAsync(res);
        Assert.Equal(email.ToLowerInvariant(), info.GetProperty("email").GetString());
        Assert.Equal("Juan", info.GetProperty("firstName").GetString());
        Assert.Equal("Goon", info.GetProperty("level").GetString());
        Assert.Equal("Test Board", info.GetProperty("inviterName").GetString());
        Assert.True(info.GetProperty("expiresAt").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddHours(47));

        Assert.Equal(HttpStatusCode.NotFound, (await factory.NewClient().GetAsync("/api/auth/invite/nope")).StatusCode);
    }

    [Fact]
    public async Task Send_password_reset_queues_email_for_active_account()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var goon = await factory.CreateUserAsync(PersonLevel.Goon);
        Assert.Equal(HttpStatusCode.NoContent, (await board.PostAsync($"/api/people/{goon.PersonId}/send-password-reset")).StatusCode);
        await factory.ProcessOutboxAsync();
        var token = factory.Emails.LastToken(goon.Email, "reset");
        Assert.Equal(HttpStatusCode.NoContent, (await factory.NewClient().PostAsync("/api/auth/password/reset", new { token, password = "reset-by-admin-123" })).StatusCode);
        Assert.True(await factory.DbAsync(db => db.AuditEntries.AnyAsync(a => a.Action == "person.password_reset_sent" && a.EntityId == goon.PersonId.ToString())));

        var member = await factory.CreateUserAsync(PersonLevel.Member);
        Assert.Equal(HttpStatusCode.Conflict, (await board.PostAsync($"/api/people/{member.PersonId}/send-password-reset")).StatusCode);
        var pending = await factory.CreateUserAsync(PersonLevel.Goon, withPassword: false);
        Assert.Equal(HttpStatusCode.Conflict, (await board.PostAsync($"/api/people/{pending.PersonId}/send-password-reset")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await board.PostAsync($"/api/people/{Guid.NewGuid()}/send-password-reset")).StatusCode);
    }

    [Fact]
    public async Task Deactivate_marks_person_and_user_and_audits()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var target = await factory.CreateUserAsync(PersonLevel.Goon);

        Assert.Equal(HttpStatusCode.NoContent, (await board.PostAsync($"/api/people/{target.PersonId}/deactivate")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await board.PostAsync($"/api/people/{target.PersonId}/deactivate")).StatusCode);

        var list = await ApiClient.JsonAsync(await board.GetAsync($"/api/people?search={Uri.EscapeDataString(target.Email)}"));
        var item = list.GetProperty("items")[0];
        Assert.Equal("Deactivated", item.GetProperty("accountStatus").GetString());
        Assert.False(item.GetProperty("isActive").GetBoolean());

        Assert.False(await factory.DbAsync(db => db.Users.Where(u => u.Id == target.UserId).Select(u => u.IsActive).SingleAsync()));
        Assert.True(await factory.DbAsync(db => db.PersonHistory.AnyAsync(h => h.PersonId == target.PersonId && h.Kind == PersonHistoryKinds.Deactivated)));
        Assert.True(await factory.DbAsync(db => db.AuditEntries.AnyAsync(a => a.Action == "person.deactivate" && a.EntityId == target.PersonId.ToString())));

        // Still on the roster (no hard delete), and forgot-password does nothing for them.
        var before = await factory.DbAsync(db => db.Outbox.CountAsync());
        await factory.NewClient().PostAsync("/api/auth/password/forgot", new { email = target.Email });
        Assert.Equal(before, await factory.DbAsync(db => db.Outbox.CountAsync()));
    }

    [Fact]
    public async Task Board_cannot_deactivate_self()
    {
        var self = await factory.CreateUserAsync(PersonLevel.Board);
        var client = factory.NewClient();
        await client.LoginOkAsync(self.Email, HtnApiFactory.Password);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/people/{self.PersonId}/deactivate")).StatusCode);
    }

    [Fact]
    public async Task Audit_entries_are_append_only_in_the_database()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        await board.PostAsync("/api/people", NewPerson("Member"));
        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            factory.DbAsync(db => db.Database.ExecuteSqlRawAsync("UPDATE audit_entries SET reason = 'tampered'")));
        Assert.Contains("append-only", ex.Message, StringComparison.Ordinal);
    }
}
