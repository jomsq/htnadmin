using Htn.Api.Common;
using Microsoft.AspNetCore.Antiforgery;

namespace Htn.Api.Auth;

/// <summary>
/// Validates the <c>X-CSRF-TOKEN</c> header on every state-changing <c>/api</c> request (including login).
/// Endpoints opt out with <c>.DisableAntiforgery()</c> (e.g. signed webhooks).
/// </summary>
public sealed class AntiforgeryValidationMiddleware(RequestDelegate next, IAntiforgery antiforgery)
{
    public const string HeaderName = "X-CSRF-TOKEN";
    public const string Code = "antiforgery";

    public async Task InvokeAsync(HttpContext context)
    {
        var method = context.Request.Method;
        var safe = HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method);
        if (!safe && context.Request.Path.StartsWithSegments("/api"))
        {
            var meta = context.GetEndpoint()?.Metadata.GetMetadata<IAntiforgeryMetadata>();
            if (meta is not { RequiresValidation: false })
            {
                try
                {
                    await antiforgery.ValidateRequestAsync(context);
                }
                catch (AntiforgeryValidationException)
                {
                    await Problems.WriteAsync(context, StatusCodes.Status400BadRequest,
                        "Security token missing or invalid",
                        "Refresh the page and try again.", Code);
                    return;
                }
            }
        }

        await next(context);
    }
}
