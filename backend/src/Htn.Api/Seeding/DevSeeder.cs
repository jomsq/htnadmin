using Htn.Domain.Events;
using Htn.Domain.Organization;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Seeding;

/// <summary>
/// Development-only, idempotent sample data (including a Board account with a published password). Runs only when
/// the environment is Development AND <c>Seed:Dev=true</c>, so a mis-set ASPNETCORE_ENVIRONMENT alone doesn't seed it.
/// </summary>
public static class DevSeeder
{
    public const string DevPassword = "Dev-Password-123!";
    public const string ConfigKey = "Seed:Dev";

    public static bool ShouldRun(IHostEnvironment env, IConfiguration config) =>
        env.IsDevelopment() && config.GetValue<bool>(ConfigKey);

    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<HtnDbContext>();
        var users = sp.GetRequiredService<UserManager<AppUser>>();
        var clock = sp.GetRequiredService<TimeProvider>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DevSeeder));

        await EnsureAsync(db, users, clock, "Alex", "Reyes", "alex@hackthenorth.ph", PersonLevel.Board, "Chairperson", ct);
        await EnsureAsync(db, users, clock, "Mika", "Soriano", "mika@hackthenorth.ph", PersonLevel.Goon, "Head of Logistics", ct);
        await EnsureAsync(db, users, clock, "Paolo", "Garcia", "paolo@hackthenorth.ph", PersonLevel.Member, null, ct);
        await EnsureAsync(db, users, clock, "Leo", "Navarro", "leo@hackthenorth.ph", PersonLevel.Board, "Treasurer", ct);
        await EnsurePositionsAsync(db, clock, ct);
        await EnsureEventsAsync(db, clock, ct);
        logger.LogInformation("Development seed data is in place");
    }

    /// <summary>Sample goon positions from the design canvas. Board seats come from the migration in every environment.</summary>
    public static readonly string[] SampleGoonPositions =
        ["Head of Logistics", "Head of Programs", "Head of Marketing", "Head of Partnerships", "Head of Tech"];

    private static async Task EnsurePositionsAsync(HtnDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var existing = await db.Positions.Where(p => p.Kind == PositionKind.GoonPosition).Select(p => p.Title).ToListAsync(ct);
        var order = existing.Count;
        foreach (var title in SampleGoonPositions.Where(t => !existing.Contains(t)))
            db.Positions.Add(Position.Create(PositionKind.GoonPosition, title, ++order, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
    }

    private static async Task EnsureAsync(
        HtnDbContext db, UserManager<AppUser> users, TimeProvider clock,
        string first, string last, string email, PersonLevel level, string? position, CancellationToken ct)
    {
        if (await db.People.AnyAsync(p => p.Email == email, ct)) return;
        var now = clock.GetUtcNow();
        var person = Person.Create(first, null, last, email, level, position, now, actorId: null);
        db.People.Add(person);
        await db.SaveChangesAsync(ct);

        if (!level.HasLogin()) return;
        var user = new AppUser
        {
            Id = Guid.CreateVersion7(now),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            PersonId = person.Id,
            CreatedAt = now,
            LockoutEnabled = true,
        };
        var result = await users.CreateAsync(user, DevPassword);
        if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(user, Roles.For(level));
    }

    public const string SampleEventSlug = "htn-con-2026";
    public const string SampleDraftSlug = "student-ctf";

    /// <summary>
    /// Sample events from the design canvas: HTN Con 2026 on sale with four ticket types and STUDENT20, and the
    /// Student CTF draft. Times are Asia/Manila (+08:00). Skipped per event when its slug already exists.
    /// </summary>
    private static async Task EnsureEventsAsync(HtnDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var manila = TimeSpan.FromHours(8);
        if (!await db.Events.AnyAsync(e => e.Slug == SampleEventSlug, ct))
        {
            var con = Event.Create("HTN Con 2026", SampleEventSlug,
                new DateTimeOffset(2026, 11, 22, 8, 0, 0, manila), new DateTimeOffset(2026, 11, 22, 18, 0, 0, manila),
                "SMX Convention Center, Pasay", 400,
                "The yearly HackTheNorth.PH conference: talks, workshops and villages for the security community.", now, null);
            con.UpdateRegistrationSettings(manualRegistration: true, groupRegistration: true, publicPage: true, ProofMode.Upload,
                isMainEvent: true, membershipFee: 300m, membershipYear: 2026, now, null);
            db.Events.Add(con);

            DateTimeOffset salesStart = new(2026, 10, 16, 0, 0, 0, manila), salesEnd = new(2026, 11, 20, 23, 59, 0, manila);
            var regular = TicketType.Create(con.Id, "Regular", 1500m, 200, 1, salesStart, salesEnd, TicketVisibility.Public, 1, now);
            var student = TicketType.Create(con.Id, "Student", 800m, 40, 1, salesStart, salesEnd, TicketVisibility.Public, 2, now);
            var team = TicketType.Create(con.Id, "Team of 5", 6000m, 10, 5, salesStart, salesEnd, TicketVisibility.Public, 3, now);
            var comp = TicketType.Create(con.Id, "Sponsor comp", 0m, 30, 1, null, null, TicketVisibility.Hidden, 4, now);
            db.TicketTypes.AddRange(regular, student, team, comp);
            db.Vouchers.Add(Voucher.Create(con.Id, "STUDENT20", VoucherKind.Percent, 20m, [student.Id], 50,
                new DateTimeOffset(2026, 11, 15, 23, 59, 0, manila), VoucherSource.Manual, now));

            if (con.EndsAt > now) con.Publish(hasActiveTicket: true, now, null); // after the event date the sample stays a draft
            await db.SaveChangesAsync(ct);
        }

        if (!await db.Events.AnyAsync(e => e.Slug == SampleDraftSlug, ct))
        {
            db.Events.Add(Event.Create("Student CTF", SampleDraftSlug,
                new DateTimeOffset(2027, 1, 17, 9, 0, 0, manila), new DateTimeOffset(2027, 1, 17, 18, 0, 0, manila),
                "UP Diliman, Quezon City", 200, "A capture-the-flag competition for student teams.", now, null));
            await db.SaveChangesAsync(ct);
        }
    }
}
