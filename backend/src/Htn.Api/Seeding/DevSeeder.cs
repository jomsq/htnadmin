using Htn.Domain.Common;
using Htn.Domain.Committees;
using Htn.Domain.Events;
using Htn.Domain.Registrations;
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
        // Frame 06: a committee member and someone left in the "Not yet assigned" pool.
        await EnsureAsync(db, users, clock, "Ana", "Bautista", "ana@hackthenorth.ph", PersonLevel.Apprentice, null, ct);
        await EnsureAsync(db, users, clock, "Ken", "Morales", "ken@hackthenorth.ph", PersonLevel.Goon, "Head of Tech", ct);
        await EnsurePositionsAsync(db, clock, ct);
        await EnsureEventsAsync(db, clock, ct);
        await EnsureRegistrationsAsync(db, sp.GetRequiredService<IPublicIdGenerator>(), clock, ct);
        await EnsureCommitteesAsync(db, clock, ct);
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
        var person = Person.Create(first, null, last, email, level, position, now, actorId: null, reason: MemberRule.SeededReason);
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

    /// <summary>The six committees of frame 06, in the order the design shows them.</summary>
    public static readonly string[] SampleCommittees =
        ["Logistics", "Programs", "Partnerships", "Marketing", "Registration & Check-in", "Tech & AV"];

    /// <summary>
    /// Committees of HTN Con 2026 (frame 06): the six teams, Mika Soriano leading Logistics with Ana Bautista as a
    /// member, and two volunteers (one on Logistics, one left in the pool). Each piece is skipped when it already
    /// exists, so re-seeding changes nothing.
    /// </summary>
    private static async Task EnsureCommitteesAsync(HtnDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var con = await db.Events.AsNoTracking().SingleOrDefaultAsync(e => e.Slug == SampleEventSlug, ct);
        if (con is null) return;
        var now = clock.GetUtcNow();

        var existing = await db.Committees.Where(c => c.EventId == con.Id).ToListAsync(ct);
        var order = existing.Count == 0 ? 0 : existing.Max(c => c.SortOrder);
        foreach (var name in SampleCommittees.Where(n => !existing.Any(c => c.Name == n)))
        {
            var committee = Committee.Create(con.Id, name, ++order, now);
            db.Committees.Add(committee);
            existing.Add(committee);
        }
        await db.SaveChangesAsync(ct);

        var logistics = existing.Single(c => c.Name == "Logistics");
        var mika = await db.People.AsNoTracking().Where(p => p.Email == "mika@hackthenorth.ph").Select(p => (Guid?)p.Id).SingleOrDefaultAsync(ct);
        var ana = await db.People.AsNoTracking().Where(p => p.Email == "ana@hackthenorth.ph").Select(p => (Guid?)p.Id).SingleOrDefaultAsync(ct);
        foreach (var (personId, role) in new[] { (mika, CommitteeRole.Lead), (ana, CommitteeRole.Member) })
        {
            if (personId is not { } id) continue;
            if (await db.CommitteeMembers.AnyAsync(m => m.EventId == con.Id && m.PersonId == id && m.LeftAt == null, ct)) continue;
            db.CommitteeMembers.Add(CommitteeMember.Join(logistics, id, role, now));
            if (role == CommitteeRole.Lead) logistics.SetLead(id);
        }

        foreach (var (name, email, committee) in new (string, string?, Committee?)[]
                 {
                     ("Carla Diaz", "carla.diaz@example.ph", logistics),
                     ("Pia Santos", null, null),
                 })
        {
            if (await db.EventVolunteers.AnyAsync(v => v.EventId == con.Id && v.Name == name, ct)) continue;
            var volunteer = EventVolunteer.Create(con.Id, name, email, "+639171234567", now);
            volunteer.AttachTo(committee);
            db.EventVolunteers.Add(volunteer);
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Sample registrations on HTN Con 2026 across statuses (pending, overdue, paid, paid with STUDENT20, a team of 5,
    /// a sponsor comp, cancelled), from frame 09. Skipped when the event already has registrations. No emails are queued.
    /// </summary>
    private static async Task EnsureRegistrationsAsync(HtnDbContext db, IPublicIdGenerator ids, TimeProvider clock, CancellationToken ct)
    {
        var con = await db.Events.AsNoTracking().SingleOrDefaultAsync(e => e.Slug == SampleEventSlug, ct);
        if (con is null || await db.Registrations.AnyAsync(r => r.EventId == con.Id, ct)) return;
        var tickets = await db.TicketTypes.AsNoTracking().Where(t => t.EventId == con.Id).ToDictionaryAsync(t => t.Name, ct);
        var student20 = await db.Vouchers.AsNoTracking().SingleOrDefaultAsync(v => v.EventId == con.Id && v.Code == "STUDENT20", ct);
        if (!tickets.TryGetValue("Regular", out var regular) || !tickets.TryGetValue("Student", out var student)
            || !tickets.TryGetValue("Team of 5", out var team) || !tickets.TryGetValue("Sponsor comp", out var comp)) return;

        var now = clock.GetUtcNow();
        static AttendeeDetails P(string first, string last, string email, string? company = null, string? shirt = "M") =>
            new(first, null, last, "Prefer not to say", email, "+639171234567", null, company, shirt, "Facebook");

        var admin = await db.Users.AsNoTracking().Where(u => u.Email == "alex@hackthenorth.ph").Select(u => (Guid?)u.Id).SingleOrDefaultAsync(ct);

        Registration Add(TicketType t, AttendeeDetails[] people, Voucher? v, DateTimeOffset at, Guid? manualBy = null)
        {
            var reg = Registration.Create(con.Id, t, people, people.Select(_ => ids.NewParticipantId()).ToList(), v,
                manualBy is null ? RegistrationSource.Public : RegistrationSource.Manual, manualBy, RegistrationForm.ConsentVersion,
                at.AddHours(48), null, at);
            db.Registrations.Add(reg);
            return reg;
        }

        Add(regular, [P("Maria", "Santos", "maria.santos@example.ph", "DICT Region 1")], null, now.AddHours(-2));
        Add(regular, [P("Jose", "Dela Cruz", "jose.delacruz@example.ph", "Mariano Marcos State University")], null, now.AddDays(-3)); // overdue
        var paid = Add(regular, [P("Ana", "Reyes", "ana.reyes@example.ph", "Ilocos Norte Provincial Government", "S")], null, now.AddDays(-5));
        db.Payments.Add(paid.MarkPaid(PaymentMethod.GCash, null, "Ref 7741 0923", now.AddDays(-4), null, now.AddDays(-4)));
        if (student20 is not null)
        {
            var stu = Add(student, [P("Carlo", "Mendoza", "carlo.mendoza@example.ph", "Northwestern University", "L")], student20, now.AddDays(-2));
            db.Payments.Add(stu.MarkPaid(PaymentMethod.Bank, null, "BPI 0045 1182", now.AddDays(-1), null, now.AddDays(-1)));
        }
        Add(team,
        [
            P("Bea", "Garcia", "bea.garcia@example.ph", "Team Hack Ilocos"), P("Dan", "Lim", "dan.lim@example.ph", "Team Hack Ilocos"),
            P("Eli", "Ramos", "eli.ramos@example.ph", "Team Hack Ilocos"), P("Fe", "Torres", "fe.torres@example.ph", "Team Hack Ilocos"),
            P("Gab", "Navarro", "gab.navarro@example.ph", "Team Hack Ilocos"),
        ], null, now.AddHours(-20));
        if (admin is not null)
        {
            var sponsor = Add(comp, [P("Hana", "Villanueva", "hana.villanueva@example.ph", "Sample Sponsor Inc.")], null, now.AddDays(-6), admin);
            db.Payments.Add(sponsor.MarkPaid(PaymentMethod.Comp, null, null, null, admin, now.AddDays(-6)));
        }
        var cancelled = Add(regular, [P("Ivan", "Castillo", "ivan.castillo@example.ph")], null, now.AddDays(-7));
        cancelled.Cancel("Duplicate registration (sample data).", null, now.AddDays(-6));
        await db.SaveChangesAsync(ct);
    }
}
