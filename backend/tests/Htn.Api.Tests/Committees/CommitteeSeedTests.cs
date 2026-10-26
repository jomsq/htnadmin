using Htn.Api.Seeding;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Committees;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Tests.Committees;

/// <summary>The dev seed puts the six committees of frame 06 on HTN Con 2026, and re-running it changes nothing.</summary>
public sealed class CommitteeSeedTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task Dev_seed_adds_the_six_committees_with_a_lead_a_member_and_two_volunteers_once()
    {
        await DevSeeder.SeedAsync(factory.Services);
        await DevSeeder.SeedAsync(factory.Services);

        var con = await factory.DbAsync(db => db.Events.AsNoTracking().SingleAsync(e => e.Slug == DevSeeder.SampleEventSlug));
        var committees = await factory.DbAsync(db => db.Committees.AsNoTracking()
            .Where(c => c.EventId == con.Id).OrderBy(c => c.SortOrder).ToListAsync());
        Assert.Equal(DevSeeder.SampleCommittees, committees.Select(c => c.Name).ToArray());
        Assert.All(committees, c => Assert.True(c.IsActive));

        var logistics = committees.Single(c => c.Name == "Logistics");
        var mika = await factory.DbAsync(db => db.People.AsNoTracking().SingleAsync(p => p.Email == "mika@hackthenorth.ph"));
        var ana = await factory.DbAsync(db => db.People.AsNoTracking().SingleAsync(p => p.Email == "ana@hackthenorth.ph"));
        Assert.Equal(mika.Id, logistics.LeadPersonId);

        var members = await factory.DbAsync(db => db.CommitteeMembers.AsNoTracking()
            .Where(m => m.EventId == con.Id && m.LeftAt == null).ToListAsync());
        Assert.Equal(2, members.Count);
        Assert.Equal(CommitteeRole.Lead, members.Single(m => m.PersonId == mika.Id).Role);
        Assert.Equal(CommitteeRole.Member, members.Single(m => m.PersonId == ana.Id).Role);
        Assert.All(members, m => Assert.Equal(logistics.Id, m.CommitteeId));

        var volunteers = await factory.DbAsync(db => db.EventVolunteers.AsNoTracking()
            .Where(v => v.EventId == con.Id).OrderBy(v => v.Name).ToListAsync());
        Assert.Equal(["Carla Diaz", "Pia Santos"], volunteers.Select(v => v.Name).ToArray());
        Assert.Equal(logistics.Id, volunteers[0].CommitteeId);
        Assert.Null(volunteers[1].CommitteeId);
    }
}
