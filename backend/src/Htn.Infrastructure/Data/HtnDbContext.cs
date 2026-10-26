using Htn.Domain.Audit;
using Htn.Domain.Events;
using Htn.Domain.Files;
using Htn.Domain.Organization;
using Htn.Domain.Outbox;
using Htn.Domain.Settings;
using Htn.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Htn.Infrastructure.Data;

public sealed class HtnDbContext(DbContextOptions<HtnDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<Person> People => Set<Person>();
    public DbSet<PersonHistory> PersonHistory => Set<PersonHistory>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<PositionAssignment> PositionAssignments => Set<PositionAssignment>();
    public DbSet<OrgSettings> OrgSettings => Set<OrgSettings>();
    public DbSet<PaymentAccount> PaymentAccounts => Set<PaymentAccount>();
    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<TicketType> TicketTypes => Set<TicketType>();
    public DbSet<Voucher> Vouchers => Set<Voucher>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<AccountToken> AccountTokens => Set<AccountToken>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(HtnDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Money is always decimal(12,2) (PHP).
        configurationBuilder.Properties<decimal>().HavePrecision(12, 2);
        configurationBuilder.Properties<decimal?>().HavePrecision(12, 2);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardAppendOnly();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardAppendOnly();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void GuardAppendOnly()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Modified or EntityState.Deleted)) continue;
            if (entry.Entity is AuditEntry or Domain.Organization.PersonHistory)
                throw new InvalidOperationException($"{entry.Entity.GetType().Name} is append-only.");
            if (entry.State == EntityState.Deleted && entry.Entity is Person)
                throw new InvalidOperationException("People are deactivated, never deleted.");
            if (entry.State == EntityState.Deleted && entry.Entity is Position or PositionAssignment or PaymentAccount or StoredFile or Domain.Settings.OrgSettings
                    or Event or TicketType or Voucher)
                throw new InvalidOperationException($"{entry.Entity.GetType().Name} rows are never deleted.");
        }
    }
}
