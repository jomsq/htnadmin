using Htn.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;

namespace Htn.Api.Common;

/// <summary>A rejected domain transition becomes 409 ProblemDetails with the domain message.</summary>
public sealed class DomainExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DomainException domain) return false;
        await Problems.WriteAsync(httpContext, StatusCodes.Status409Conflict, "Not allowed", domain.Message, "domain_rule");
        return true;
    }
}
