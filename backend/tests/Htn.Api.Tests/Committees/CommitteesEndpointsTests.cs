using System.Net;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Committees;
using Htn.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.Committees.CommitteeTestData;
using static Htn.Api.Tests.Events.EventTestData;

namespace Htn.Api.Tests.Committees;

public sealed class CommitteesEndpointsTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task Board_creates_renames_and_lists_committees()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var eventId = await OnSaleAsync(board);

        var logistics = await AddCommitteeAsync(board, eventId, "Logistics");
        await AddCommitteeAsync(board, eventId, "Programs");

        // Names are unique per event, case-insensitively.
        var duplicate = await board.PostAsync($"/api/events/{eventId}/committees", NewCommittee("logistics"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("duplicate_name", (await ApiClient.JsonAsync(duplicate)).Code());

        var renamed = await board.PutAsync($"/api/events/{eventId}/committees/{logistics}", NewCommittee("Logistics & Venue", 5));
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var card = await ApiClient.JsonAsync(renamed);
        Assert.Equal("Logistics & Venue", card.GetProperty("name").GetString());
        Assert.Equal(5, card.GetProperty("sortOrder").GetInt32());
        Assert.True(card.GetProperty("canManage").GetBoolean());
        Assert.Equal(0, card.GetProperty("openTasks").GetInt32()); // no tasks on this committee yet

        var page = await PageAsync(board, eventId);
        Assert.True(page.GetProperty("canCreateCommittee").GetBoolean());
        Assert.True(page.GetProperty("canManageVolunteers").GetBoolean());
        Assert.Equal(2, page.GetProperty("committees").GetArrayLength());
        Assert.Equal(["Programs", "Logistics & Venue"],
            page.GetProperty("committees").EnumerateArray().Select(c => c.GetProperty("name").GetString()).ToList());

        // A short name is a 400, not a 500.
        var invalid = await board.PostAsync($"/api/events/{eventId}/committees", new { name = "", sortOrder = (int?)null });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.True((await ApiClient.JsonAsync(invalid)).HasError("name"));
    }

    [Fact]
    public async Task The_lead_must_be_an_active_goon_and_leads_one_committee_per_event()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var eventId = await OnSaleAsync(board);
        var logistics = await AddCommitteeAsync(board, eventId, "Logistics");
        var programs = await AddCommitteeAsync(board, eventId, "Programs");

        var (apprentice, _) = await SignedInUserAsync(factory, PersonLevel.Apprentice);
        var member = await factory.CreateUserAsync(PersonLevel.Member);
        foreach (var personId in new[] { apprentice.PersonId, member.PersonId })
        {
            var res = await board.PutAsync($"/api/events/{eventId}/committees/{logistics}/lead", new { personId });
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            var problem = await ApiClient.JsonAsync(res);
            Assert.Equal("wrong_level", problem.Code());
            Assert.True(problem.HasError("personId"));
        }

        var (goon, _) = await SignedInUserAsync(factory, PersonLevel.Goon);
        await SetLeadAsync(board, eventId, logistics, goon.PersonId);
        var page = await PageAsync(board, eventId);
        Assert.Equal(goon.PersonId, page.Committee(logistics).GetProperty("lead").GetProperty("personId").GetGuid());
        Assert.Equal("Goon", page.Committee(logistics).GetProperty("lead").GetProperty("level").GetString());
        Assert.Empty(page.Committee(logistics).MemberIds());   // the lead is not repeated in members
        Assert.Equal(1, page.Committee(logistics).GetProperty("peopleCount").GetInt32());
        Assert.DoesNotContain(goon.PersonId, page.PoolPeople());

        // One committee per person, so leading a second one is refused.
        var second = await board.PutAsync($"/api/events/{eventId}/committees/{programs}/lead", new { personId = goon.PersonId });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("already_lead", (await ApiClient.JsonAsync(second)).Code());

        // Replacing the lead keeps the old one on the committee as a plain member.
        var (next, _) = await SignedInUserAsync(factory, PersonLevel.Goon);
        await SetLeadAsync(board, eventId, logistics, next.PersonId);
        page = await PageAsync(board, eventId);
        Assert.Equal(next.PersonId, page.Committee(logistics).GetProperty("lead").GetProperty("personId").GetGuid());
        Assert.Equal([goon.PersonId], page.Committee(logistics).MemberIds());

        // Clearing the lead leaves them on the committee, too.
        await SetLeadAsync(board, eventId, logistics, null);
        page = await PageAsync(board, eventId);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, page.Committee(logistics).GetProperty("lead").ValueKind);
        Assert.Equal(2, page.Committee(logistics).MemberIds().Count);
    }

    [Fact]
    public async Task A_person_is_on_at_most_one_committee_per_event()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var eventId = await OnSaleAsync(board);
        var a = await AddCommitteeAsync(board, eventId, "Logistics");
        var b = await AddCommitteeAsync(board, eventId, "Programs");
        var (person, _) = await SignedInUserAsync(factory, PersonLevel.Apprentice);

        Assert.Equal(HttpStatusCode.OK,
            (await board.PostAsync($"/api/events/{eventId}/committees/{a}/members", new { personId = person.PersonId })).StatusCode);
        var clash = await board.PostAsync($"/api/events/{eventId}/committees/{b}/members", new { personId = person.PersonId });
        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
        Assert.Equal("already_in_committee", (await ApiClient.JsonAsync(clash)).Code());

        // Adding them again to the same committee is a no-op, not a duplicate row.
        Assert.Equal(HttpStatusCode.OK,
            (await board.PostAsync($"/api/events/{eventId}/committees/{a}/members", new { personId = person.PersonId })).StatusCode);
        Assert.Equal(1, await factory.DbAsync(db => db.CommitteeMembers.CountAsync(m => m.PersonId == person.PersonId && m.LeftAt == null)));

        // After leaving they can join another committee, and the history row stays.
        Assert.Equal(HttpStatusCode.NoContent,
            (await board.DeleteAsync($"/api/events/{eventId}/committees/{a}/members/{person.PersonId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await board.PostAsync($"/api/events/{eventId}/committees/{b}/members", new { personId = person.PersonId })).StatusCode);
        var rows = await factory.DbAsync(db => db.CommitteeMembers.AsNoTracking()
            .Where(m => m.PersonId == person.PersonId).OrderBy(m => m.JoinedAt).ToListAsync());
        Assert.Equal(2, rows.Count);
        Assert.NotNull(rows[0].LeftAt);
        Assert.Null(rows[1].LeftAt);
    }

    [Fact]
    public async Task Only_people_with_an_active_login_can_be_added()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var eventId = await OnSaleAsync(board);
        var committee = await AddCommitteeAsync(board, eventId, "Logistics");

        var member = await factory.CreateUserAsync(PersonLevel.Member); // roster only, no login
        var wrong = await board.PostAsync($"/api/events/{eventId}/committees/{committee}/members", new { personId = member.PersonId });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal("wrong_level", (await ApiClient.JsonAsync(wrong)).Code());

        var unknown = await board.PostAsync($"/api/events/{eventId}/committees/{committee}/members", new { personId = Guid.CreateVersion7() });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        // Board members are admins everywhere but may still be put on a committee.
        var (boardPerson, _) = await SignedInUserAsync(factory, PersonLevel.Board);
        Assert.Equal(HttpStatusCode.OK,
            (await board.PostAsync($"/api/events/{eventId}/committees/{committee}/members", new { personId = boardPerson.PersonId })).StatusCode);
    }

    [Fact]
    public async Task Removing_a_member_records_leaving_and_refuses_the_lead()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var eventId = await OnSaleAsync(board);
        var committee = await AddCommitteeAsync(board, eventId, "Logistics");
        var (lead, _) = await SignedInUserAsync(factory, PersonLevel.Goon);
        var (helper, _) = await SignedInUserAsync(factory, PersonLevel.Apprentice);
        await SetLeadAsync(board, eventId, committee, lead.PersonId);
        await board.PostAsync($"/api/events/{eventId}/committees/{committee}/members", new { personId = helper.PersonId });

        var refused = await board.DeleteAsync($"/api/events/{eventId}/committees/{committee}/members/{lead.PersonId}");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("is_lead", (await ApiClient.JsonAsync(refused)).Code());

        Assert.Equal(HttpStatusCode.NoContent,
            (await board.DeleteAsync($"/api/events/{eventId}/committees/{committee}/members/{helper.PersonId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await board.DeleteAsync($"/api/events/{eventId}/committees/{committee}/members/{helper.PersonId}")).StatusCode);

        var row = await factory.DbAsync(db => db.CommitteeMembers.AsNoTracking().SingleAsync(m => m.PersonId == helper.PersonId));
        Assert.NotNull(row.LeftAt);
        var page = await PageAsync(board, eventId);
        Assert.Empty(page.Committee(committee).MemberIds());
        Assert.Contains(helper.PersonId, page.PoolPeople());
    }

    [Fact]
    public async Task The_pool_lists_unassigned_goons_and_apprentices_only()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var eventId = await OnSaleAsync(board);
        var committee = await AddCommitteeAsync(board, eventId, "Logistics");
        var (goon, _) = await SignedInUserAsync(factory, PersonLevel.Goon);
        var (apprentice, _) = await SignedInUserAsync(factory, PersonLevel.Apprentice);
        var (boardPerson, _) = await SignedInUserAsync(factory, PersonLevel.Board);
        var rosterOnly = await factory.CreateUserAsync(PersonLevel.Member);
        var assigned = await factory.CreateUserAsync(PersonLevel.Goon);
        await board.PostAsync($"/api/events/{eventId}/committees/{committee}/members", new { personId = assigned.PersonId });

        var page = await PageAsync(board, eventId);
        var pool = page.PoolPeople();
        Assert.Contains(goon.PersonId, pool);
        Assert.Contains(apprentice.PersonId, pool);
        Assert.DoesNotContain(boardPerson.PersonId, pool);  // admins everywhere, never pooled
        Assert.DoesNotContain(rosterOnly.PersonId, pool);   // no login
        Assert.DoesNotContain(assigned.PersonId, pool);     // already on a committee

        // A deactivated person drops out of the pool.
        await board.PostAsync($"/api/people/{goon.PersonId}/deactivate", new { reason = "Left the org." });
        Assert.DoesNotContain(goon.PersonId, (await PageAsync(board, eventId)).PoolPeople());
    }

    [Fact]
    public async Task Deactivating_a_committee_is_board_only_and_empties_it()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var eventId = await OnSaleAsync(board);
        var committee = await AddCommitteeAsync(board, eventId, "Logistics");
        var (lead, leadClient) = await SignedInUserAsync(factory, PersonLevel.Goon);
        var (helper, _) = await SignedInUserAsync(factory, PersonLevel.Apprentice);
        await SetLeadAsync(board, eventId, committee, lead.PersonId);
        await board.PostAsync($"/api/events/{eventId}/committees/{committee}/members", new { personId = helper.PersonId });
        var volunteer = (await ApiClient.JsonAsync(await board.PostAsync($"/api/events/{eventId}/volunteers",
            Volunteer("Carla Diaz", committeeId: committee)))).GetProperty("id").GetGuid();

        // The lead manages the people on their committee, not the committee itself.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await leadClient.PostAsync($"/api/events/{eventId}/committees/{committee}/deactivate", new { reason = "No" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await board.PostAsync($"/api/events/{eventId}/committees/{committee}/deactivate", new { reason = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await board.PostAsync($"/api/events/{eventId}/committees/{committee}/deactivate", new { reason = "Merged into Programs." })).StatusCode);

        var page = await PageAsync(board, eventId);
        Assert.False(page.Committee(committee).GetProperty("isActive").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, page.Committee(committee).GetProperty("lead").ValueKind);
        Assert.Empty(page.Committee(committee).MemberIds());
        Assert.Contains(lead.PersonId, page.PoolPeople());
        Assert.Contains(helper.PersonId, page.PoolPeople());
        Assert.Contains(volunteer, page.PoolVolunteers());

        // Nothing is deleted: the membership history and the committee row are still there.
        Assert.Equal(2, await factory.DbAsync(db => db.CommitteeMembers.CountAsync(m => m.CommitteeId == committee)));
        Assert.Equal(HttpStatusCode.Conflict,
            (await board.PutAsync($"/api/events/{eventId}/committees/{committee}", NewCommittee("Logistics again"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await board.PostAsync($"/api/events/{eventId}/committees/{committee}/members", new { personId = helper.PersonId })).StatusCode);
    }

    [Fact]
    public async Task Committees_of_a_draft_event_are_hidden_from_non_managers()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var draft = (await CreateAsync(board)).Id();
        await AddCommitteeAsync(board, draft, "Logistics");
        Assert.Equal(HttpStatusCode.OK, (await board.GetAsync($"/api/events/{draft}/committees")).StatusCode);

        var (_, goon) = await SignedInUserAsync(factory, PersonLevel.Goon);
        Assert.Equal(HttpStatusCode.NotFound, (await goon.GetAsync($"/api/events/{draft}/committees")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await board.GetAsync($"/api/events/{Guid.CreateVersion7()}/committees")).StatusCode);
    }

    [Fact]
    public async Task Every_mutation_writes_an_audit_entry()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var eventId = await OnSaleAsync(board);
        var committee = await AddCommitteeAsync(board, eventId, "Audited");
        var (lead, _) = await SignedInUserAsync(factory, PersonLevel.Goon);
        var (helper, _) = await SignedInUserAsync(factory, PersonLevel.Apprentice);
        await board.PutAsync($"/api/events/{eventId}/committees/{committee}", NewCommittee("Audited twice"));
        await SetLeadAsync(board, eventId, committee, lead.PersonId);
        await board.PostAsync($"/api/events/{eventId}/committees/{committee}/members", new { personId = helper.PersonId });
        await board.DeleteAsync($"/api/events/{eventId}/committees/{committee}/members/{helper.PersonId}");
        await board.PostAsync($"/api/events/{eventId}/committees/{committee}/deactivate", new { reason = "Done with it." });

        var actions = await factory.DbAsync(db => db.AuditEntries.AsNoTracking()
            .Where(a => a.EntityId == committee.ToString() || a.Action.StartsWith("committee."))
            .Select(a => a.Action).ToListAsync());
        foreach (var action in new[]
                 {
                     "committee.create", "committee.update", "committee.set_lead", "committee.add_member",
                     "committee.remove_member", "committee.deactivate",
                 })
            Assert.Contains(action, actions);

        var deactivation = await factory.DbAsync(db => db.AuditEntries.AsNoTracking()
            .SingleAsync(a => a.Action == "committee.deactivate" && a.EntityId == committee.ToString()));
        Assert.Equal("Done with it.", deactivation.Reason);
        Assert.NotNull(deactivation.ActorId);
        Assert.NotNull(deactivation.BeforeJson);
        Assert.NotNull(deactivation.AfterJson);
    }

    [Fact]
    public async Task Committee_rows_are_never_deleted()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var eventId = await OnSaleAsync(board);
        var committee = await AddCommitteeAsync(board, eventId, "Undeletable");
        var (person, _) = await SignedInUserAsync(factory, PersonLevel.Goon);
        await board.PostAsync($"/api/events/{eventId}/committees/{committee}/members", new { personId = person.PersonId });

        await Assert.ThrowsAsync<InvalidOperationException>(() => factory.DbAsync(async db =>
        {
            db.Committees.Remove(await db.Committees.SingleAsync(c => c.Id == committee));
            return await db.SaveChangesAsync();
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => factory.DbAsync(async db =>
        {
            db.CommitteeMembers.Remove(await db.CommitteeMembers.SingleAsync(m => m.CommitteeId == committee));
            return await db.SaveChangesAsync();
        }));

        // Even past the DbContext guard, the database rejects the delete.
        var error = await Assert.ThrowsAnyAsync<Exception>(() => factory.DbAsync(db =>
            db.Database.ExecuteSqlAsync($"DELETE FROM committees WHERE id = {committee}")));
        Assert.Contains("never deleted", error.Message);
    }
}
