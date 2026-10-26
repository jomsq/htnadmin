using System.Net;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;
using static Htn.Api.Tests.Committees.CommitteeTestData;
using static Htn.Api.Tests.Events.EventTestData;

namespace Htn.Api.Tests.Committees;

/// <summary>
/// The resource-scoped side of <c>Committees.ManageMembers</c>: the board manages every committee, a lead only their
/// own, and nobody else manages anything. Reading is open to every signed-in staff role (Events.View).
/// </summary>
public sealed class CommitteeScopeTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task A_lead_manages_their_own_committee_only()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var eventId = await OnSaleAsync(board);
        var mine = await AddCommitteeAsync(board, eventId, "Logistics");
        var theirs = await AddCommitteeAsync(board, eventId, "Programs");
        var (lead, leadClient) = await SignedInUserAsync(factory, PersonLevel.Goon);
        var (other, _) = await SignedInUserAsync(factory, PersonLevel.Goon);
        await SetLeadAsync(board, eventId, mine, lead.PersonId);
        await SetLeadAsync(board, eventId, theirs, other.PersonId);
        var (helper, _) = await SignedInUserAsync(factory, PersonLevel.Apprentice);

        // Own committee: members in and out.
        Assert.Equal(HttpStatusCode.OK,
            (await leadClient.PostAsync($"/api/events/{eventId}/committees/{mine}/members", new { personId = helper.PersonId })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await leadClient.DeleteAsync($"/api/events/{eventId}/committees/{mine}/members/{helper.PersonId}")).StatusCode);

        // Someone else's committee: 403, and nothing changes.
        var denied = await leadClient.PostAsync($"/api/events/{eventId}/committees/{theirs}/members", new { personId = helper.PersonId });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal("forbidden", (await ApiClient.JsonAsync(denied)).Code());
        Assert.Equal(HttpStatusCode.Forbidden,
            (await leadClient.DeleteAsync($"/api/events/{eventId}/committees/{theirs}/members/{other.PersonId}")).StatusCode);
        Assert.Empty((await PageAsync(board, eventId)).Committee(theirs).MemberIds());

        // The committee itself stays board-only for its lead.
        Assert.Equal(HttpStatusCode.Forbidden, (await leadClient.PostAsync($"/api/events/{eventId}/committees", NewCommittee())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await leadClient.PutAsync($"/api/events/{eventId}/committees/{mine}", NewCommittee())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await leadClient.PutAsync($"/api/events/{eventId}/committees/{mine}/lead", new { personId = helper.PersonId })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await leadClient.PostAsync($"/api/events/{eventId}/committees/{mine}/deactivate", new { reason = "Mine now." })).StatusCode);

        // The lead's own view marks exactly one committee manageable.
        var page = await PageAsync(leadClient, eventId);
        Assert.False(page.GetProperty("canCreateCommittee").GetBoolean());
        Assert.True(page.GetProperty("canManageVolunteers").GetBoolean());
        Assert.True(page.Committee(mine).GetProperty("canManage").GetBoolean());
        Assert.False(page.Committee(theirs).GetProperty("canManage").GetBoolean());

        // /api/auth/me can only say "may be allowed": the lead holds the policy, a plain goon does not.
        Assert.Contains("Committees.ManageMembers", await PoliciesAsync(leadClient));
        var (_, plainGoon) = await SignedInUserAsync(factory, PersonLevel.Goon);
        Assert.DoesNotContain("Committees.ManageMembers", await PoliciesAsync(plainGoon));
    }

    [Fact]
    public async Task Apprentices_and_unprivileged_goons_can_read_but_never_write()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var eventId = await OnSaleAsync(board);
        var committee = await AddCommitteeAsync(board, eventId, "Logistics");
        var (target, _) = await SignedInUserAsync(factory, PersonLevel.Apprentice);
        var volunteerId = (await ApiClient.JsonAsync(await board.PostAsync($"/api/events/{eventId}/volunteers",
            Volunteer("Pia Santos")))).GetProperty("id").GetGuid();

        foreach (var level in new[] { PersonLevel.Apprentice, PersonLevel.Goon })
        {
            var (_, client) = await SignedInUserAsync(factory, level);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/events/{eventId}/committees")).StatusCode);

            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/events/{eventId}/committees", NewCommittee())).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsync($"/api/events/{eventId}/committees/{committee}", NewCommittee())).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.PostAsync($"/api/events/{eventId}/committees/{committee}/deactivate", new { reason = "No." })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.PutAsync($"/api/events/{eventId}/committees/{committee}/lead", new { personId = target.PersonId })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.PostAsync($"/api/events/{eventId}/committees/{committee}/members", new { personId = target.PersonId })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.DeleteAsync($"/api/events/{eventId}/committees/{committee}/members/{target.PersonId}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.PostAsync($"/api/events/{eventId}/committees/{committee}/volunteers/{volunteerId}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.DeleteAsync($"/api/events/{eventId}/committees/{committee}/volunteers/{volunteerId}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/events/{eventId}/volunteers", Volunteer())).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.PutAsync($"/api/events/{eventId}/volunteers/{volunteerId}", Volunteer())).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.PostAsync($"/api/events/{eventId}/volunteers/{volunteerId}/deactivate", new { reason = "No." })).StatusCode);

            // Read-only payloads say so.
            var page = await PageAsync(client, eventId);
            Assert.False(page.GetProperty("canCreateCommittee").GetBoolean());
            Assert.False(page.GetProperty("canManageVolunteers").GetBoolean());
            Assert.False(page.Committee(committee).GetProperty("canManage").GetBoolean());
        }

        var anonymous = factory.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/events/{eventId}/committees")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync($"/api/events/{eventId}/committees", NewCommittee())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync($"/api/events/{eventId}/volunteers", Volunteer())).StatusCode);

        Assert.Empty((await PageAsync(board, eventId)).Committee(committee).MemberIds());
    }

    /// <summary>
    /// A lead of one event's committee has no rights on another event's committees — the scope is the committee, not
    /// the role.
    /// </summary>
    [Fact]
    public async Task A_lead_has_no_rights_on_another_event()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var mineEvent = await OnSaleAsync(board);
        var otherEvent = await OnSaleAsync(board);
        var mine = await AddCommitteeAsync(board, mineEvent, "Logistics");
        var elsewhere = await AddCommitteeAsync(board, otherEvent, "Logistics");
        var (lead, leadClient) = await SignedInUserAsync(factory, PersonLevel.Goon);
        await SetLeadAsync(board, mineEvent, mine, lead.PersonId);
        var (helper, _) = await SignedInUserAsync(factory, PersonLevel.Apprentice);

        Assert.Equal(HttpStatusCode.OK,
            (await leadClient.PostAsync($"/api/events/{mineEvent}/committees/{mine}/members", new { personId = helper.PersonId })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await leadClient.PostAsync($"/api/events/{otherEvent}/committees/{elsewhere}/members", new { personId = helper.PersonId })).StatusCode);
        // A committee id from the wrong event is a 404, never a cross-event write.
        Assert.Equal(HttpStatusCode.NotFound,
            (await board.PostAsync($"/api/events/{mineEvent}/committees/{elsewhere}/members", new { personId = helper.PersonId })).StatusCode);
    }

    private static async Task<List<string>> PoliciesAsync(ApiClient client)
    {
        var me = await ApiClient.JsonAsync(await client.GetAsync("/api/auth/me"));
        return me.GetProperty("policies").EnumerateArray().Select(p => p.GetString()!).ToList();
    }
}

/// <summary>The deactivate guard, with slice 4B's open-task counts stubbed in.</summary>
public sealed class CommitteeOpenTaskGuardTests(OpenTasksApiFactory factory) : IClassFixture<OpenTasksApiFactory>
{
    [Fact]
    public async Task A_committee_with_open_tasks_cannot_be_deactivated()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var eventId = await OnSaleAsync(board);
        var committee = await AddCommitteeAsync(board, eventId, "Logistics");

        // TODO(4B): drive this from real tasks once the task board exists.
        factory.OpenTasks.Counts[committee] = 9;
        var refused = await board.PostAsync($"/api/events/{eventId}/committees/{committee}/deactivate", new { reason = "Cleaning up." });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("has_open_tasks", (await ApiClient.JsonAsync(refused)).Code());
        Assert.Equal(9, (await PageAsync(board, eventId)).Committee(committee).GetProperty("openTasks").GetInt32());

        factory.OpenTasks.Counts[committee] = 0;
        Assert.Equal(HttpStatusCode.NoContent,
            (await board.PostAsync($"/api/events/{eventId}/committees/{committee}/deactivate", new { reason = "Cleaning up." })).StatusCode);
    }
}
