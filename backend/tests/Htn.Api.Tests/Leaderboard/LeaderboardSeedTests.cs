using System.Text.Json;
using Htn.Api.Seeding;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;
using Htn.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.Leaderboard.LeaderboardTestData;

namespace Htn.Api.Tests.Leaderboard;

/// <summary>
/// Frame 14 against the dev seed: the two approved sample cards of frame 07 carry five live awards, and the sidebar
/// screen has to render them for the real cast (so the podium and the table are never empty on a fresh dev database).
/// </summary>
public sealed class LeaderboardSeedTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task Seeded_awards_show_up_on_the_leaderboard_without_the_board_members()
    {
        await DevSeeder.SeedAsync(factory.Services);
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var con = await factory.DbAsync(db => db.Events.AsNoTracking().SingleAsync(e => e.Slug == DevSeeder.SampleEventSlug));

        var page = await GetAsync(board, $"?scope=event&eventId={con.Id}");

        var awards = await factory.DbAsync(db => db.PointsAwards.AsNoTracking()
            .Where(a => a.EventId == con.Id && a.ReversedAt == null).ToListAsync());
        var ranked = await factory.DbAsync(db => db.People.AsNoTracking()
            .Where(p => p.IsActive && (p.Level == PersonLevel.Goon || p.Level == PersonLevel.Apprentice))
            .Select(p => p.Id).ToListAsync());

        Assert.NotEmpty(page.Rows());
        // Every listed person is a goon or apprentice with live seeded awards, and the points add up row by row.
        foreach (var row in page.Rows())
        {
            var personId = row.GetProperty("personId").GetGuid();
            Assert.Contains(personId, ranked);
            var mine = awards.Where(a => a.PersonId == personId).ToList();
            Assert.Equal(mine.Sum(a => a.Points), row.Points());
            Assert.Equal(mine.Select(a => a.TaskId).Distinct().Count(), row.GetProperty("tasksDone").GetInt32());
            Assert.Equal(mine.Count(a => a.Role == TaskAssigneeRole.Lead), row.GetProperty("asLead").GetInt32());
            Assert.Equal(mine.Count(a => a.Role == TaskAssigneeRole.Collab), row.GetProperty("asCollab").GetInt32());
            Assert.Equal(mine.Count(a => a.Role == TaskAssigneeRole.Assistant), row.GetProperty("asAssistant").GetInt32());
            Assert.NotEqual("", row.GetProperty("committees").GetString());
        }
        // Ranks are dense and never skip, highest points first.
        var ranks = page.Rows().Select(r => r.Rank()).ToList();
        Assert.Equal(1, ranks[0]);
        Assert.True(ranks.Zip(ranks.Skip(1)).All(p => p.Second - p.First is 0 or 1));
        Assert.Equal(page.Rows().Select(r => r.Points()).OrderByDescending(p => p), page.Rows().Select(r => r.Points()));
        // The seeded admin approved the cards but never appears: board members are not ranked.
        Assert.DoesNotContain(JsonValueKind.Null, page.Rows().Select(r => r.GetProperty("level").ValueKind));
        Assert.All(page.Rows(), r => Assert.Contains(r.GetProperty("level").GetString(), new[] { "Goon", "Apprentice" }));
    }
}
