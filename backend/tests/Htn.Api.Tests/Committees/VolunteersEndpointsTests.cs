using System.Net;
using System.Text.Json;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.Committees.CommitteeTestData;
using static Htn.Api.Tests.Events.EventTestData;

namespace Htn.Api.Tests.Committees;

public sealed class VolunteersEndpointsTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task Board_adds_edits_and_deactivates_a_volunteer()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var eventId = await OnSaleAsync(board);
        var committee = await AddCommitteeAsync(board, eventId, "Logistics");

        var created = await board.PostAsync($"/api/events/{eventId}/volunteers",
            Volunteer("Carla Diaz", "Carla.Diaz@example.ph", "0917 123 4567", committee));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var dto = await ApiClient.JsonAsync(created);
        var id = dto.GetProperty("id").GetGuid();
        Assert.Equal("Carla Diaz", dto.GetProperty("name").GetString());
        Assert.Equal("carla.diaz@example.ph", dto.GetProperty("email").GetString());
        Assert.Equal("+639171234567", dto.GetProperty("mobile").GetString());

        var page = await PageAsync(board, eventId);
        Assert.Equal([id], page.Committee(committee).GetProperty("volunteers").EnumerateArray()
            .Select(v => v.GetProperty("id").GetGuid()).ToList());
        Assert.Equal(1, page.Committee(committee).GetProperty("peopleCount").GetInt32());
        Assert.DoesNotContain(id, page.PoolVolunteers());

        // Editing can also take them off the committee, back into the pool.
        var updated = await board.PutAsync($"/api/events/{eventId}/volunteers/{id}", Volunteer("Carla D. Diaz"));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("Carla D. Diaz", (await ApiClient.JsonAsync(updated)).GetProperty("name").GetString());
        Assert.Contains(id, (await PageAsync(board, eventId)).PoolVolunteers());

        Assert.Equal(HttpStatusCode.BadRequest,
            (await board.PostAsync($"/api/events/{eventId}/volunteers/{id}/deactivate", new { reason = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await board.PostAsync($"/api/events/{eventId}/volunteers/{id}/deactivate", new { reason = "Did not show up." })).StatusCode);
        Assert.DoesNotContain(id, (await PageAsync(board, eventId)).PoolVolunteers());
        Assert.Equal(HttpStatusCode.Conflict,
            (await board.PutAsync($"/api/events/{eventId}/volunteers/{id}", Volunteer("Carla Diaz"))).StatusCode);

        // Nothing is deleted.
        var row = await factory.DbAsync(db => db.EventVolunteers.AsNoTracking().SingleAsync(v => v.Id == id));
        Assert.False(row.IsActive);
        Assert.NotNull(row.DeactivatedAt);
        Assert.Null(row.CommitteeId);
        var audits = await factory.DbAsync(db => db.AuditEntries.AsNoTracking()
            .Where(a => a.EntityId == id.ToString()).Select(a => a.Action).ToListAsync());
        Assert.Contains("volunteer.create", audits);
        Assert.Contains("volunteer.update", audits);
        Assert.Contains("volunteer.deactivate", audits);
    }

    [Theory]
    [InlineData("Ana Bautista")]
    [InlineData("Ma. Niña Dela Cruz-Ñañez")]
    [InlineData("O'Brien")]
    [InlineData("Santos, Jr.")]
    public async Task Volunteer_names_follow_the_attendee_name_rules(string name)
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var eventId = await OnSaleAsync(board);
        var res = await board.PostAsync($"/api/events/{eventId}/volunteers", Volunteer(name));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Carla 2")]
    [InlineData("<b>Carla</b>")]
    [InlineData("https://evil.example/x")]
    [InlineData("carla@example.ph")]
    [InlineData("Ma.Niña")]
    public async Task Bad_volunteer_names_are_refused(string name)
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var eventId = await OnSaleAsync(board);
        var res = await board.PostAsync($"/api/events/{eventId}/volunteers", Volunteer(name));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.True((await ApiClient.JsonAsync(res)).HasError("name"));
    }

    [Fact]
    public async Task A_long_name_an_odd_email_and_an_odd_mobile_are_refused()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var eventId = await OnSaleAsync(board);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await board.PostAsync($"/api/events/{eventId}/volunteers", Volunteer(new string('a', 121)))).StatusCode);
        var email = await board.PostAsync($"/api/events/{eventId}/volunteers", Volunteer("Carla Diaz", "not-an-email"));
        Assert.Equal(HttpStatusCode.BadRequest, email.StatusCode);
        Assert.True((await ApiClient.JsonAsync(email)).HasError("email"));
        var mobile = await board.PostAsync($"/api/events/{eventId}/volunteers", Volunteer("Carla Diaz", null, "12345"));
        Assert.Equal(HttpStatusCode.BadRequest, mobile.StatusCode);
        Assert.True((await ApiClient.JsonAsync(mobile)).HasError("mobile"));
    }

    [Fact]
    public async Task A_lead_manages_volunteers_on_their_own_committee_only()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var eventId = await OnSaleAsync(board);
        var mine = await AddCommitteeAsync(board, eventId, "Logistics");
        var theirs = await AddCommitteeAsync(board, eventId, "Programs");
        var (lead, leadClient) = await SignedInUserAsync(factory, PersonLevel.Goon);
        await SetLeadAsync(board, eventId, mine, lead.PersonId);

        // On their own committee: create, attach, detach.
        var created = await leadClient.PostAsync($"/api/events/{eventId}/volunteers", Volunteer("Joy Castro", committeeId: mine));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await ApiClient.JsonAsync(created)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent,
            (await leadClient.DeleteAsync($"/api/events/{eventId}/committees/{mine}/volunteers/{id}")).StatusCode);
        // Unattached volunteers are board-only, so the lead can no longer touch this one.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await leadClient.PutAsync($"/api/events/{eventId}/volunteers/{id}", Volunteer("Joy Castro", committeeId: mine))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await leadClient.PostAsync($"/api/events/{eventId}/volunteers/{id}/deactivate", new { reason = "No." })).StatusCode);
        // The board can hand it back.
        Assert.Equal(HttpStatusCode.OK, (await board.PostAsync($"/api/events/{eventId}/committees/{mine}/volunteers/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await leadClient.PostAsync($"/api/events/{eventId}/volunteers/{id}/deactivate", new { reason = "Cannot make it." })).StatusCode);

        // Another committee: never.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await leadClient.PostAsync($"/api/events/{eventId}/volunteers", Volunteer("Noel Sy", committeeId: theirs))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await leadClient.PostAsync($"/api/events/{eventId}/volunteers", Volunteer("Ria Chua"))).StatusCode);
        var onTheirs = (await ApiClient.JsonAsync(await board.PostAsync($"/api/events/{eventId}/volunteers",
            Volunteer("Dan Perez", committeeId: theirs)))).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden,
            (await leadClient.PostAsync($"/api/events/{eventId}/committees/{mine}/volunteers/{onTheirs}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await leadClient.PutAsync($"/api/events/{eventId}/volunteers/{onTheirs}", Volunteer("Dan Perez", committeeId: mine))).StatusCode);
    }

    [Fact]
    public async Task Volunteer_contact_details_are_only_shown_to_managers()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var eventId = await OnSaleAsync(board);
        var committee = await AddCommitteeAsync(board, eventId, "Logistics");
        await board.PostAsync($"/api/events/{eventId}/volunteers", Volunteer("Carla Diaz", "carla@example.ph", "09171234567", committee));

        var (_, goon) = await SignedInUserAsync(factory, PersonLevel.Goon);
        var card = (await PageAsync(goon, eventId)).Committee(committee).GetProperty("volunteers")[0];
        Assert.Equal("Carla Diaz", card.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, card.GetProperty("email").ValueKind);
        Assert.Equal(JsonValueKind.Null, card.GetProperty("mobile").ValueKind);

        var boardCard = (await PageAsync(board, eventId)).Committee(committee).GetProperty("volunteers")[0];
        Assert.Equal("carla@example.ph", boardCard.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Creating_a_volunteer_is_denied_before_anything_can_reveal_what_exists()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var eventId = await OnSaleAsync(board);
        var mine = await AddCommitteeAsync(board, eventId, "Logistics");
        var theirs = await AddCommitteeAsync(board, eventId, "Programs");
        var (lead, leadClient) = await SignedInUserAsync(factory, PersonLevel.Goon);
        await SetLeadAsync(board, eventId, mine, lead.PersonId);

        // A committee the lead doesn't run, one that doesn't exist, and an event that doesn't exist: all 403, so the
        // answer never says which of them is real.
        foreach (var body in new[]
                 {
                     Volunteer("Ria Chua", committeeId: theirs),
                     Volunteer("Ria Chua", committeeId: Guid.CreateVersion7()),
                     Volunteer("Ria Chua"),
                 })
            Assert.Equal(HttpStatusCode.Forbidden,
                (await leadClient.PostAsync($"/api/events/{eventId}/volunteers", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await leadClient.PostAsync($"/api/events/{Guid.CreateVersion7()}/volunteers",
                Volunteer("Ria Chua", committeeId: theirs))).StatusCode);

        // Her own committee still works, and the board still gets the honest 404 for a missing committee.
        Assert.Equal(HttpStatusCode.Created,
            (await leadClient.PostAsync($"/api/events/{eventId}/volunteers", Volunteer("Ria Chua", committeeId: mine))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await board.PostAsync($"/api/events/{eventId}/volunteers",
                Volunteer("Ray Cruz", committeeId: Guid.CreateVersion7()))).StatusCode);
    }

    [Fact]
    public async Task Attaching_fails_for_unknown_rows_and_deactivated_targets()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var eventId = await OnSaleAsync(board);
        var committee = await AddCommitteeAsync(board, eventId, "Logistics");
        var other = await OnSaleAsync(board);
        var id = (await ApiClient.JsonAsync(await board.PostAsync($"/api/events/{eventId}/volunteers",
            Volunteer("Pia Santos")))).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound,
            (await board.PostAsync($"/api/events/{eventId}/committees/{committee}/volunteers/{Guid.CreateVersion7()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await board.PostAsync($"/api/events/{other}/volunteers", Volunteer("X Y", committeeId: committee))).StatusCode);
        // Detaching one that isn't on this committee is a 404, not a silent success.
        Assert.Equal(HttpStatusCode.NotFound,
            (await board.DeleteAsync($"/api/events/{eventId}/committees/{committee}/volunteers/{id}")).StatusCode);

        await board.PostAsync($"/api/events/{eventId}/volunteers/{id}/deactivate", new { reason = "Withdrew." });
        var refused = await board.PostAsync($"/api/events/{eventId}/committees/{committee}/volunteers/{id}");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("volunteer_inactive", (await ApiClient.JsonAsync(refused)).Code());
    }
}
