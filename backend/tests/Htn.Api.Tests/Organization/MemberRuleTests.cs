using System.Net;
using System.Text;
using System.Text.Json;
using Htn.Api.Tests.CheckIn;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.CheckIn.CheckInTestData;
using static Htn.Api.Tests.Registrations.RegistrationTestData;

namespace Htn.Api.Tests.Organization;

/// <summary>To become a Member a person must have checked in at an event (matched by email); the import is exempt.</summary>
public sealed class MemberRuleTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    private static object NewPerson(string level, string email) => new { firstName = "Paolo", lastName = "Garcia", email, level };

    [Fact]
    public async Task Creating_a_member_needs_a_check_in_with_the_same_email()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var email = HtnApiFactory.UniqueEmail("newmember");

        var refused = await board.PostAsync("/api/people", NewPerson("Member", email));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("no_check_in", (await ApiClient.JsonAsync(refused)).GetProperty("code").GetString());
        Assert.False(await factory.DbAsync(db => db.People.AnyAsync(p => p.Email == email)));

        // Registered (and paid) but never checked in is not enough.
        var ev = await LiveAsync(board);
        await PaidAsync(factory, board, ev, email: email);
        Assert.Equal("no_check_in", (await ApiClient.JsonAsync(await board.PostAsync("/api/people", NewPerson("Member", email)))).GetProperty("code").GetString());

        // Other levels are not affected.
        Assert.Equal(HttpStatusCode.Created, (await board.PostAsync("/api/people", NewPerson("Apprentice", HtnApiFactory.UniqueEmail("appr")))).StatusCode);
    }

    [Fact]
    public async Task Creating_a_member_is_allowed_after_a_check_in_matching_case_insensitively()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var email = HtnApiFactory.UniqueEmail("walkin");
        var ev = await LiveAsync(board);
        var guest = await PaidAsync(factory, board, ev, email: email.ToUpperInvariant());
        Assert.Equal("Welcome", (await ScanOkAsync(board, ev.Id, guest.Token)).Result());

        var res = await board.PostAsync("/api/people", NewPerson("Member", email));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.Equal("Member", (await ApiClient.JsonAsync(res)).GetProperty("level").GetString());
    }

    [Fact]
    public async Task Demoting_to_member_needs_a_check_in()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var apprentice = await factory.CreateUserAsync(PersonLevel.Apprentice);
        var url = $"/api/people/{apprentice.PersonId}/promote";

        var refused = await board.PostAsync(url, new { toLevel = "Member", reason = "Stepping back" });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("no_check_in", (await ApiClient.JsonAsync(refused)).GetProperty("code").GetString());
        Assert.Equal(PersonLevel.Apprentice, await factory.DbAsync(db => db.People.Where(p => p.Id == apprentice.PersonId).Select(p => p.Level).SingleAsync()));
        Assert.False(await factory.DbAsync(db => db.AuditEntries.AnyAsync(a => a.Action == "person.demote" && a.EntityId == apprentice.PersonId.ToString())));

        await factory.SeedCheckInAsync(apprentice.Email.ToUpperInvariant());
        var ok = await board.PostAsync(url, new { toLevel = "Member", reason = "Stepping back" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("Member", (await ApiClient.JsonAsync(ok)).GetProperty("level").GetString());
    }

    private static object Update(string email) => new { firstName = "Paolo", lastName = "Garcia", email };

    [Fact]
    public async Task Changing_a_members_email_needs_a_check_in_for_the_new_address()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var email = HtnApiFactory.UniqueEmail("member");
        await factory.SeedCheckInAsync(email);
        var created = await ApiClient.JsonAsync(await board.PostAsync("/api/people", NewPerson("Member", email)));
        var id = created.GetProperty("id").GetGuid();

        var unchecked_ = HtnApiFactory.UniqueEmail("typo");
        var refused = await board.PutAsync($"/api/people/{id}", Update(unchecked_));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("no_check_in", (await ApiClient.JsonAsync(refused)).GetProperty("code").GetString());
        Assert.Equal(email, await factory.DbAsync(db => db.People.Where(p => p.Id == id).Select(p => p.Email).SingleAsync()));

        var checkedIn = HtnApiFactory.UniqueEmail("fixed");
        await factory.SeedCheckInAsync(checkedIn.ToUpperInvariant());
        Assert.Equal(HttpStatusCode.OK, (await board.PutAsync($"/api/people/{id}", Update(checkedIn))).StatusCode);

        // Other levels are not affected.
        var apprentice = await factory.CreateUserAsync(PersonLevel.Apprentice, withPassword: false);
        Assert.Equal(HttpStatusCode.OK, (await board.PutAsync($"/api/people/{apprentice.PersonId}", Update(HtnApiFactory.UniqueEmail("appr")))).StatusCode);
    }

    [Fact]
    public async Task Imported_members_can_have_their_email_fixed_without_a_check_in()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var email = HtnApiFactory.UniqueEmail("imp");
        var csv = Encoding.UTF8.GetBytes($"first_name,last_name,email,level\r\nMara,Cruz,{email},Member\r\n");
        Assert.Equal(HttpStatusCode.OK, (await board.PostFileAsync("/api/people/import?dryRun=false", csv, "roster.csv", "text/csv")).StatusCode);
        var id = await factory.DbAsync(db => db.People.Where(p => p.Email == email).Select(p => p.Id).SingleAsync());
        Assert.Equal(HttpStatusCode.OK, (await board.PutAsync($"/api/people/{id}", Update(HtnApiFactory.UniqueEmail("fixedtypo")))).StatusCode);
    }

    [Fact]
    public async Task Import_is_exempt_and_history_says_imported()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var email = HtnApiFactory.UniqueEmail("imported");
        var csv = Encoding.UTF8.GetBytes($"first_name,last_name,email,level\r\nMara,Cruz,{email},Member\r\n");
        var res = await board.PostFileAsync("/api/people/import?dryRun=false", csv, "roster.csv", "text/csv");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(1, (await ApiClient.JsonAsync(res)).GetProperty("created").GetInt32());

        var person = await factory.DbAsync(db => db.People.AsNoTracking().SingleAsync(p => p.Email == email));
        Assert.Equal(PersonLevel.Member, person.Level);
        var created = await factory.DbAsync(db => db.PersonHistory.AsNoTracking().SingleAsync(h => h.PersonId == person.Id && h.Kind == PersonHistoryKinds.Created));
        Assert.Equal(MemberRule.ImportedReason, created.Reason);

        var detail = await ApiClient.JsonAsync(await board.GetAsync($"/api/people/{person.Id}"));
        Assert.Equal("imported", detail.GetProperty("history")[0].GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Person_detail_lists_attendance_for_board_only()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var apprentice = await factory.CreateUserAsync(PersonLevel.Apprentice);
        var ev = await LiveAsync(board);
        var guest = await PaidAsync(factory, board, ev, email: apprentice.Email.ToUpperInvariant());
        await ScanOkAsync(board, ev.Id, guest.Token);
        var seededEvent = await factory.SeedCheckInAsync(apprentice.Email, "Earlier meetup");
        // Paid at a third event but never checked in: not attendance.
        var third = await LiveAsync(board);
        await PaidAsync(factory, board, third, email: apprentice.Email);

        var detail = await ApiClient.JsonAsync(await board.GetAsync($"/api/people/{apprentice.PersonId}"));
        var attendance = detail.GetProperty("attendance").EnumerateArray().ToList();
        Assert.Equal(2, attendance.Count);
        Assert.Equal(new[] { ev.Id, seededEvent }.Order(), attendance.Select(a => a.GetProperty("eventId").GetGuid()).Order());
        Assert.Contains(attendance, a => a.GetProperty("eventName").GetString() == "Earlier meetup");
        Assert.All(attendance, a => Assert.NotEqual(JsonValueKind.Null, a.GetProperty("checkedInAt").ValueKind));
        var times = attendance.Select(a => a.GetProperty("checkedInAt").GetDateTimeOffset()).ToList();
        Assert.Equal(times.OrderByDescending(t => t), times);

        var goon = await factory.SignedInAsync(PersonLevel.Goon);
        var limited = await ApiClient.JsonAsync(await goon.GetAsync($"/api/people/{apprentice.PersonId}"));
        Assert.Empty(limited.GetProperty("attendance").EnumerateArray());

        // The PersonDto shape is unchanged.
        Assert.Equal(
            ["accountStatus", "createdAt", "email", "firstName", "id", "isActive", "isCfo", "isFinanceAssistant", "lastName", "lastSignInAt", "level", "middleName", "position"],
            detail.GetProperty("person").EnumerateObject().Select(p => p.Name).Order());
    }
}
