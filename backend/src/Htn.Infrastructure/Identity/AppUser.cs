using Htn.Domain.Organization;
using Microsoft.AspNetCore.Identity;

namespace Htn.Infrastructure.Identity;

/// <summary>A login. Every user is linked to a <see cref="Person"/> on the roster.</summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public Guid? PersonId { get; set; }
    public Person? Person { get; set; }

    /// <summary>Exactly one board member is the CFO (finance posting/approval).</summary>
    public bool IsCfo { get; set; }

    /// <summary>At most one goon is the finance assistant (entries stay Pending).</summary>
    public bool IsFinanceAssistant { get; set; }

    /// <summary>False once deactivated; deactivated users can't sign in.</summary>
    public bool IsActive { get; set; } = true;

    public UserPreferences Preferences { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastSignInAt { get; set; }
}
