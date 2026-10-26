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
        logger.LogInformation("Development seed data is in place");
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
}
