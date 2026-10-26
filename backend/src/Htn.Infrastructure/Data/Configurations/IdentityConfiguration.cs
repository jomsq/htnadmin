using Htn.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Htn.Infrastructure.Data.Configurations;

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> b)
    {
        b.ToTable("users");
        b.HasIndex(u => u.PersonId).IsUnique().HasFilter("person_id IS NOT NULL");
        // Identity only enforces unique emails in code; make the database enforce it too.
        b.HasIndex(u => u.NormalizedEmail).HasDatabaseName("EmailIndex").IsUnique();
        b.HasOne(u => u.Person).WithMany().HasForeignKey(u => u.PersonId).OnDelete(DeleteBehavior.Restrict);
        b.Property(u => u.IsActive).HasDefaultValue(true);
        b.ComplexProperty(u => u.Preferences, p =>
        {
            p.Property(x => x.Mode).HasConversion<string>().HasMaxLength(16).HasColumnName("pref_mode");
            p.Property(x => x.TextSize).HasConversion<string>().HasMaxLength(16).HasColumnName("pref_text_size");
            p.Property(x => x.Density).HasConversion<string>().HasMaxLength(16).HasColumnName("pref_density");
        });
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<IdentityRole<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityRole<Guid>> b)
    {
        b.ToTable("roles");
        b.HasData(
            Role(Roles.BoardId, Roles.Board),
            Role(Roles.GoonId, Roles.Goon),
            Role(Roles.ApprenticeId, Roles.Apprentice));
    }

    private static IdentityRole<Guid> Role(Guid id, string name) => new()
    {
        Id = id,
        Name = name,
        NormalizedName = name.ToUpperInvariant(),
        ConcurrencyStamp = id.ToString(),
    };
}

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<IdentityUserRole<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserRole<Guid>> b) => b.ToTable("user_roles");
}

internal sealed class UserClaimConfiguration : IEntityTypeConfiguration<IdentityUserClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserClaim<Guid>> b) => b.ToTable("user_claims");
}

internal sealed class UserLoginConfiguration : IEntityTypeConfiguration<IdentityUserLogin<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserLogin<Guid>> b) => b.ToTable("user_logins");
}

internal sealed class UserTokenConfiguration : IEntityTypeConfiguration<IdentityUserToken<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserToken<Guid>> b) => b.ToTable("user_tokens");
}

internal sealed class RoleClaimConfiguration : IEntityTypeConfiguration<IdentityRoleClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityRoleClaim<Guid>> b) => b.ToTable("role_claims");
}

internal sealed class AccountTokenConfiguration : IEntityTypeConfiguration<AccountToken>
{
    public void Configure(EntityTypeBuilder<AccountToken> b)
    {
        b.ToTable("account_tokens");
        b.HasKey(t => t.Id);
        b.Property(t => t.Id).ValueGeneratedNever();
        b.Property(t => t.Purpose).HasConversion<string>().HasMaxLength(32);
        b.Property(t => t.TokenHash).HasMaxLength(64).IsFixedLength();
        b.HasIndex(t => t.TokenHash).IsUnique();
        b.HasIndex(t => new { t.UserId, t.Purpose });
        b.HasOne<AppUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_account_tokens_users_user_id");
        b.HasOne<AppUser>().WithMany().HasForeignKey(t => t.IssuedById).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_account_tokens_users_issued_by_id");
    }
}
