using FluentValidation;
using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Api.Features.Auth;
using Htn.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace Htn.Api.Features.Settings;

public sealed class PreferencesRequestValidator : AbstractValidator<PreferencesDto>
{
    public static readonly string[] Modes = ["light", "system", "dark"];
    public static readonly string[] TextSizes = ["default", "large"];
    public static readonly string[] Densities = ["comfortable", "compact"];

    public PreferencesRequestValidator()
    {
        RuleFor(x => x.Mode).Must(v => Modes.Contains(v?.ToLowerInvariant())).WithMessage("Choose light, system or dark.");
        RuleFor(x => x.TextSize).Must(v => TextSizes.Contains(v?.ToLowerInvariant())).WithMessage("Choose default or large.");
        RuleFor(x => x.Density).Must(v => Densities.Contains(v?.ToLowerInvariant())).WithMessage("Choose comfortable or compact.");
    }
}

public static class PreferencesEndpoints
{
    public static RouteGroupBuilder MapPreferences(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/me").WithTags("Settings");
        g.MapPut("/preferences", Update).RequireAuthorization(Policies.SettingsPersonal).Validate<PreferencesDto>();
        return g;
    }

    private static async Task<IResult> Update(PreferencesDto req, HttpContext http, UserManager<AppUser> users)
    {
        var user = await users.GetUserAsync(http.User);
        if (user is null) return TypedResults.Unauthorized();
        user.Preferences.Mode = Enum.Parse<ThemeMode>(req.Mode, ignoreCase: true);
        user.Preferences.TextSize = Enum.Parse<TextSize>(req.TextSize, ignoreCase: true);
        user.Preferences.Density = Enum.Parse<Density>(req.Density, ignoreCase: true);
        await users.UpdateAsync(user);
        return TypedResults.NoContent();
    }
}
