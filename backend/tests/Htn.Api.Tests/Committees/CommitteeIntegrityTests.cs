using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.Committees.CommitteeTestData;
using static Htn.Api.Tests.Events.EventTestData;

namespace Htn.Api.Tests.Committees;

/// <summary>
/// The database half of the Committees migration: the append-only / no-delete triggers, the case-insensitive unique
/// name per event, the one-active-committee-per-person and one-lead-per-committee indexes, and the REVOKEs. These
/// hold even for SQL that never goes through the API.
/// </summary>
public sealed class CommitteeIntegrityTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task The_guards_and_revokes_are_in_place()
    {
        foreach (var trigger in new[]
                 {
                     "committees_guard", "committees_no_truncate", "committee_members_guard", "committee_members_no_truncate",
                     "event_volunteers_guard", "event_volunteers_no_truncate",
                 })
            Assert.Equal(1, await factory.DbAsync(db => db.Database
                .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_trigger WHERE tgname = {trigger}").SingleAsync()));

        foreach (var table in new[] { "committees", "committee_members", "event_volunteers" })
            Assert.False(await factory.DbAsync(db => db.Database
                .SqlQuery<bool>($"SELECT has_table_privilege('public', {table}, 'DELETE') AS \"Value\"").SingleAsync()));

        // ux_committees_event_name is the lower(name) expression form.
        var definition = await factory.DbAsync(db => db.Database
            .SqlQuery<string>($"SELECT indexdef AS \"Value\" FROM pg_indexes WHERE indexname = 'ux_committees_event_name'").SingleAsync());
        Assert.Contains("lower(", definition);  // lower((name)::text) once Postgres normalizes it
    }

    [Fact]
    public async Task The_database_refuses_deletes_reactivation_and_duplicates()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var eventId = await OnSaleAsync(board);
        var committee = await AddCommitteeAsync(board, eventId, "Logistics");
        var (lead, _) = await SignedInUserAsync(factory, PersonLevel.Goon);
        var (helper, _) = await SignedInUserAsync(factory, PersonLevel.Apprentice);
        await SetLeadAsync(board, eventId, committee, lead.PersonId);
        await board.PostAsync($"/api/events/{eventId}/committees/{committee}/members", new { personId = helper.PersonId });
        var other = await AddCommitteeAsync(board, eventId, "Programs");

        await AssertFailsAsync("never deleted", $"DELETE FROM committee_members WHERE committee_id = '{committee}'");
        await AssertFailsAsync("never deleted", $"DELETE FROM committees WHERE id = '{committee}'");
        await AssertFailsAsync("immutable", $"UPDATE committees SET created_at = now() WHERE id = '{committee}'");
        await AssertFailsAsync("immutable", $"UPDATE committee_members SET person_id = '{lead.PersonId}' WHERE person_id = '{helper.PersonId}'");

        // Case-insensitive unique name per event.
        await AssertFailsAsync("ux_committees_event_name",
            $"INSERT INTO committees (id, event_id, name, sort_order, is_active, created_at) "
            + $"VALUES ('{Guid.CreateVersion7()}', '{eventId}', 'logistics', 9, true, now())");

        // One active committee per person per event, and one active lead per committee.
        await AssertFailsAsync("ux_committee_members_active_person",
            $"INSERT INTO committee_members (id, committee_id, event_id, person_id, role, joined_at) "
            + $"VALUES ('{Guid.CreateVersion7()}', '{other}', '{eventId}', '{helper.PersonId}', 'Member', now())");
        await AssertFailsAsync("ux_committee_members_active_lead",
            $"INSERT INTO committee_members (id, committee_id, event_id, person_id, role, joined_at) "
            + $"VALUES ('{Guid.CreateVersion7()}', '{committee}', '{eventId}', '{lead.PersonId}', 'Lead', now())");

        // A committee and a volunteer are never brought back to life.
        await board.PostAsync($"/api/events/{eventId}/committees/{committee}/deactivate", new { reason = "Merged." });
        await AssertFailsAsync("reactivated", $"UPDATE committees SET is_active = true, deactivated_at = NULL WHERE id = '{committee}'");

        var volunteer = (await ApiClient.JsonAsync(await board.PostAsync($"/api/events/{eventId}/volunteers",
            Volunteer("Pia Santos")))).GetProperty("id").GetGuid();
        await board.PostAsync($"/api/events/{eventId}/volunteers/{volunteer}/deactivate", new { reason = "Withdrew." });
        await AssertFailsAsync("reactivated", $"UPDATE event_volunteers SET is_active = true, deactivated_at = NULL WHERE id = '{volunteer}'");
        await AssertFailsAsync("never deleted", $"DELETE FROM event_volunteers WHERE id = '{volunteer}'");
    }

    private async Task AssertFailsAsync(string expected, string sql)
    {
        var error = await Assert.ThrowsAnyAsync<Exception>(() => factory.DbAsync(db => db.Database.ExecuteSqlRawAsync(sql)));
        Assert.Contains(expected, error.Message + error.InnerException?.Message);
    }
}
