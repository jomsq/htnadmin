using Htn.Domain.Audit;
using Htn.Domain.CheckIn;
using Htn.Domain.Committees;
using Htn.Domain.Events;
using Htn.Domain.Files;
using Htn.Domain.Organization;
using Htn.Domain.Outbox;
using Htn.Domain.Registrations;
using Htn.Domain.Settings;
using Htn.Domain.Tasks;
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
    public DbSet<Registration> Registrations => Set<Registration>();
    public DbSet<Attendee> Attendees => Set<Attendee>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentProof> PaymentProofs => Set<PaymentProof>();
    public DbSet<CheckInScan> CheckInScans => Set<CheckInScan>();
    public DbSet<Committee> Committees => Set<Committee>();
    public DbSet<CommitteeMember> CommitteeMembers => Set<CommitteeMember>();
    public DbSet<EventVolunteer> EventVolunteers => Set<EventVolunteer>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<TaskAssignee> TaskAssignees => Set<TaskAssignee>();
    public DbSet<TaskChecklistItem> TaskChecklistItems => Set<TaskChecklistItem>();
    public DbSet<TaskComment> TaskComments => Set<TaskComment>();
    public DbSet<TaskAttachment> TaskAttachments => Set<TaskAttachment>();
    public DbSet<TaskActivity> TaskActivities => Set<TaskActivity>();
    public DbSet<PointsAward> PointsAwards => Set<PointsAward>();
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
            if (entry.Entity is AuditEntry or Domain.Organization.PersonHistory or PaymentProof or CheckInScan or TaskActivity)
                throw new InvalidOperationException($"{entry.Entity.GetType().Name} is append-only.");
            // Points are append-only like finance: a reversal stamps the row (allowed), nothing is ever deleted.
            if (entry.State == EntityState.Deleted && entry.Entity is PointsAward)
                throw new InvalidOperationException("Points awards are reversed with a reason, never deleted.");
            if (entry.State == EntityState.Deleted && entry.Entity is Person)
                throw new InvalidOperationException("People are deactivated, never deleted.");
            if (entry.State == EntityState.Deleted && entry.Entity is Position or PositionAssignment or PaymentAccount or StoredFile or Domain.Settings.OrgSettings
                    or Event or TicketType or Voucher or Registration or Attendee or Payment or PaymentProof
                    or Committee or CommitteeMember or EventVolunteer
                    or TaskItem or TaskComment or TaskAttachment)
                throw new InvalidOperationException($"{entry.Entity.GetType().Name} rows are never deleted.");
        }
    }
}
