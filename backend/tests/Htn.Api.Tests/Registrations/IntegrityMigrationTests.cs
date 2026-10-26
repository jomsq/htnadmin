using Htn.Api.Seeding;
using Htn.Api.Tests.CheckIn;
using Htn.Api.Tests.Infrastructure;
using Htn.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Htn.Api.Tests.Registrations;

/// <summary>
/// Registrations_IntegrityGuards on a database that already has data: write the dev seed (all statuses, a team, a
/// comp, a cancellation) plus a check-in, roll back to CheckIn, then migrate up again. Every NOT VALID constraint must
/// VALIDATE against those rows, and the triggers and keys must be in place afterwards.
/// </summary>
public sealed class IntegrityMigrationTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    private const string Previous = "20261026094949_CheckIn";

    [Fact]
    public async Task Migration_validates_against_existing_seed_data_and_rolls_back_cleanly()
    {
        // Seed on the current schema (the dev seed also fills tables that later migrations add), then roll back.
        await DevSeeder.SeedAsync(factory.Services);
        await factory.SeedCheckInAsync(HtnApiFactory.UniqueEmail("seeded"));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HtnDbContext>();
            await db.GetService<IMigrator>().MigrateAsync(Previous);
            Assert.False(await TriggerExistsAsync(db, "registrations_guard"));
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HtnDbContext>();
            await db.Database.MigrateAsync();
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            foreach (var trigger in new[] { "registrations_guard", "attendees_guard", "payments_guard", "payments_no_truncate" })
                Assert.True(await TriggerExistsAsync(db, trigger), trigger);

            var notValidated = await db.Database.SqlQuery<string>($"""
                SELECT conname AS "Value" FROM pg_constraint
                WHERE conrelid IN ('registrations'::regclass, 'payments'::regclass, 'attendees'::regclass, 'events'::regclass,
                                   'vouchers'::regclass, 'ticket_types'::regclass)
                  AND NOT convalidated
                """).ToListAsync();
            Assert.Empty(notValidated);

            var keys = await db.Database.SqlQuery<string>($"""
                SELECT conname AS "Value" FROM pg_constraint WHERE conname IN (
                    'fk_attendees_registrations_registration_id_event_id', 'fk_registrations_ticket_types_ticket_type_id_event_id',
                    'fk_registrations_vouchers_voucher_id_event_id', 'ak_registrations_id_event_id')
                """).ToListAsync();
            Assert.Equal(4, keys.Count);
            Assert.True(await db.Registrations.CountAsync() >= 8);
        }
    }

    private static async Task<bool> TriggerExistsAsync(HtnDbContext db, string name) =>
        (await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_trigger WHERE tgname = {name}").SingleAsync()) == 1;
}
