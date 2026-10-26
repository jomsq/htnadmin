using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Common;

/// <summary>A lost optimistic-concurrency race (row version changed since it was read) becomes 409 <c>concurrent_change</c>.</summary>
public sealed class ConcurrencyExceptionHandler : IExceptionHandler
{
    public const string Code = "concurrent_change";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DbUpdateConcurrencyException) return false;
        await Problems.WriteAsync(httpContext, StatusCodes.Status409Conflict, "Someone else changed this at the same time",
            "Reload and try again.", Code);
        return true;
    }
}
