using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Htn.Infrastructure.Identity;

/// <summary>Adds HTN-specific claims (person, CFO flag, 2FA state) to the cookie principal.</summary>
public sealed class HtnClaimsPrincipalFactory(
    UserManager<AppUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<AppUser, IdentityRole<Guid>>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (user.PersonId is { } personId) identity.AddClaim(new Claim(HtnClaimTypes.PersonId, personId.ToString()));
        if (user.IsCfo) identity.AddClaim(new Claim(HtnClaimTypes.Cfo, "true"));
        if (user.IsFinanceAssistant) identity.AddClaim(new Claim(HtnClaimTypes.FinanceAssistant, "true"));
        identity.AddClaim(new Claim(HtnClaimTypes.TwoFactorEnabled, user.TwoFactorEnabled ? "true" : "false"));
        return identity;
    }
}
