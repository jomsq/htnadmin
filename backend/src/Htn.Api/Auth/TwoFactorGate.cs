using Htn.Api.Common;
using Htn.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace Htn.Api.Auth;

/// <summary>Marks endpoints a board user without 2FA may still call (me, logout, 2FA setup, antiforgery...).</summary>
public sealed class AllowWithoutTwoFactorAttribute : Attribute;

public static class TwoFactorGateExtensions
{
    public static TBuilder AllowWithoutTwoFactor<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new AllowWithoutTwoFactorAttribute());
}

/// <summary>
/// Board users without TOTP are signed in, but everything except the allow-listed endpoints
/// returns 403 with <c>code = "2fa_required"</c>.
/// </summary>
public sealed class TwoFactorGateMiddleware(RequestDelegate next, IOptionsMonitor<AuthSettings> settings)
{
    public const string Code = "2fa_required";

    public async Task InvokeAsync(HttpContext context)
    {
        var user = context.User;
        if (settings.CurrentValue.RequireBoard2fa
            && user.Identity?.IsAuthenticated == true
            && user.IsInRole(Roles.Board)
            && user.FindFirst(HtnClaimTypes.TwoFactorEnabled)?.Value != "true"
            && context.GetEndpoint()?.Metadata.GetMetadata<AllowWithoutTwoFactorAttribute>() is null)
        {
            await Problems.WriteAsync(context, StatusCodes.Status403Forbidden,
                "Two-step verification required",
                "Board accounts must turn on two-step verification before continuing.", Code);
            return;
        }

        await next(context);
    }
}
