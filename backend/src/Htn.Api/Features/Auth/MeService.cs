using System.Security.Claims;
using Htn.Api.Auth;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Htn.Api.Features.Auth;

public sealed class MeService(HtnDbContext db, IAuthorizationService authorization, IOptionsMonitor<AuthSettings> settings)
{
    public async Task<MeDto> BuildAsync(AppUser user, ClaimsPrincipal principal, CancellationToken ct)
    {
        var person = user.PersonId is { } pid
            ? await db.People.AsNoTracking().SingleOrDefaultAsync(p => p.Id == pid, ct)
            : null;

        var granted = new List<string>();
        foreach (var policy in Policies.All)
            if ((await authorization.AuthorizeAsync(principal, policy)).Succeeded)
                granted.Add(policy);

        var isBoard = principal.IsInRole(Roles.Board);
        return new MeDto(
            user.Id,
            user.PersonId,
            person?.FirstName ?? "",
            person?.LastName ?? "",
            user.Email ?? "",
            person?.Level.ToString(),
            user.IsCfo,
            user.TwoFactorEnabled,
            TwoFactorRequired: isBoard && settings.CurrentValue.RequireBoard2fa && !user.TwoFactorEnabled,
            granted,
            ToDto(user.Preferences));
    }

    public static PreferencesDto ToDto(UserPreferences p) =>
        new(p.Mode.ToString().ToLowerInvariant(), p.TextSize.ToString().ToLowerInvariant(), p.Density.ToString().ToLowerInvariant());
}
