using Htn.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Htn.Infrastructure.Data.Configurations;

internal sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> b)
    {
        b.ToTable("people", t =>
        {
            t.HasCheckConstraint("ck_people_level", CheckConstraints.In("level", Enum.GetNames<PersonLevel>()));
            // The unique index on email is only case-insensitive if every row is stored normalized.
            t.HasCheckConstraint("ck_people_email_normalized", "email = lower(btrim(email))");
            t.HasCheckConstraint("ck_people_deactivated_at",
                "(is_active AND deactivated_at IS NULL) OR (NOT is_active AND deactivated_at IS NOT NULL)");
        });
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).ValueGeneratedNever();
        b.Property(p => p.FirstName).HasMaxLength(Person.NameMaxLength).IsRequired();
        b.Property(p => p.MiddleName).HasMaxLength(Person.NameMaxLength);
        b.Property(p => p.LastName).HasMaxLength(Person.NameMaxLength).IsRequired();
        b.Property(p => p.Email).HasMaxLength(Person.EmailMaxLength).IsRequired();
        b.Property(p => p.Level).HasConversion<string>().HasMaxLength(16);
        b.Property(p => p.Position).HasMaxLength(Person.PositionMaxLength);
        b.Ignore(p => p.FullName);

        // Email is stored normalized (trim + lower), so a plain unique index is case-insensitive.
        b.HasIndex(p => p.Email).IsUnique();
        b.HasIndex(p => p.Level);
        b.HasIndex(p => new { p.LastName, p.FirstName });

        b.HasMany(p => p.History).WithOne().HasForeignKey(h => h.PersonId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(p => p.History).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class PersonHistoryConfiguration : IEntityTypeConfiguration<PersonHistory>
{
    public void Configure(EntityTypeBuilder<PersonHistory> b)
    {
        b.ToTable("person_history", t => t.HasCheckConstraint("ck_person_history_kind", CheckConstraints.In("kind",
            PersonHistoryKinds.Created, PersonHistoryKinds.LevelChanged, PersonHistoryKinds.PositionChanged, PersonHistoryKinds.Deactivated)));
        b.HasKey(h => h.Id);
        b.Property(h => h.Id).ValueGeneratedNever();
        b.Property(h => h.Kind).HasMaxLength(32).IsRequired();
        b.Property(h => h.FromValue).HasMaxLength(200);
        b.Property(h => h.ToValue).HasMaxLength(200);
        b.Property(h => h.Reason).HasMaxLength(500);
        b.HasIndex(h => new { h.PersonId, h.At });
    }
}
