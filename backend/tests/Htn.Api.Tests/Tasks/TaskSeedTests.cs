using Htn.Api.Seeding;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Tests.Tasks;

/// <summary>
/// The dev seed puts the eleven sample cards of frame 07 on HTN Con 2026, spread over the five columns and the six
/// committees, with the two Done cards approved so the leaderboard has real awards. Re-running it changes nothing.
/// </summary>
public sealed class TaskSeedTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task Dev_seed_adds_the_eleven_sample_tasks_once_with_awards_on_the_finished_ones()
    {
        await DevSeeder.SeedAsync(factory.Services);
        await DevSeeder.SeedAsync(factory.Services);

        var con = await factory.DbAsync(db => db.Events.AsNoTracking().SingleAsync(e => e.Slug == DevSeeder.SampleEventSlug));
        var tasks = await factory.DbAsync(db => db.Tasks.AsNoTracking().Where(t => t.EventId == con.Id).ToListAsync());
        Assert.Equal(11, tasks.Count);
        Assert.Equal(Enumerable.Range(1, 11), tasks.Select(t => t.Number).Order());
        Assert.Equal(11, tasks.Select(t => t.Reference).Distinct().Count());
        Assert.All(tasks, t => Assert.False(t.IsArchived));

        // Every column of frame 07 has cards, and every one of the six committees is represented.
        Assert.Equal([2, 3, 3, 1, 2],
            TaskViewsColumns.Select(status => tasks.Count(t => t.Status == status)).ToList());
        var committees = await factory.DbAsync(db => db.Committees.AsNoTracking()
            .Where(c => c.EventId == con.Id).ToDictionaryAsync(c => c.Id, c => c.Name));
        Assert.Equal(DevSeeder.SampleCommittees.Order(),
            tasks.Select(t => committees[t.CommitteeId]).Distinct().Order());

        // Checklists, assignees and one volunteer helper.
        var ids = tasks.Select(t => t.Id).ToList();
        Assert.True(await factory.DbAsync(db => db.TaskChecklistItems.AsNoTracking().CountAsync(i => ids.Contains(i.TaskId))) >= 40);
        var assignees = await factory.DbAsync(db => db.TaskAssignees.AsNoTracking().Where(a => ids.Contains(a.TaskId)).ToListAsync());
        Assert.All(tasks, t => Assert.Contains(assignees, a => a.TaskId == t.Id && a.Role == TaskAssigneeRole.Lead));
        Assert.Single(assignees, a => a.VolunteerId is not null);

        // The two Done cards are approved and hold live awards; no volunteer is ever awarded.
        var done = tasks.Where(t => t.Status == TaskBoardStatus.Done).ToList();
        Assert.Equal(2, done.Count);
        Assert.All(done, t => Assert.NotNull(t.ApprovedAt));
        var awards = await factory.DbAsync(db => db.PointsAwards.AsNoTracking().Where(a => ids.Contains(a.TaskId)).ToListAsync());
        Assert.Equal(5, awards.Count); // 3 on "Send sponsor contracts" + 2 on "Book venue"
        Assert.All(awards, a => Assert.Null(a.ReversedAt));
        Assert.All(awards, a => Assert.Equal(decimal.Round(a.BasePoints * a.Multiplier, 2), a.Points));
        Assert.Equal(awards.Select(a => a.TaskId).Distinct().Order(), done.Select(t => t.Id).Order());
        // Open cards have no awards at all.
        Assert.DoesNotContain(awards, a => tasks.Single(t => t.Id == a.TaskId).Status != TaskBoardStatus.Done);
    }

    private static readonly TaskBoardStatus[] TaskViewsColumns =
        [TaskBoardStatus.Backlog, TaskBoardStatus.ToDo, TaskBoardStatus.InProgress, TaskBoardStatus.Review, TaskBoardStatus.Done];
}
