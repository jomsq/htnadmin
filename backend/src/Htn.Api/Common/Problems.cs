using Microsoft.AspNetCore.Mvc;

namespace Htn.Api.Common;

public static class Problems
{
    public static async Task WriteAsync(HttpContext context, int status, string title, string? detail = null, string? code = null)
    {
        context.Response.StatusCode = status;
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        if (code is not null) problem.Extensions["code"] = code;
        var service = context.RequestServices.GetRequiredService<IProblemDetailsService>();
        if (!await service.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem }))
            await context.Response.WriteAsJsonAsync(problem);
    }

    public static IResult Result(int status, string title, string? detail = null, string? code = null) =>
        TypedResults.Problem(statusCode: status, title: title, detail: detail,
            extensions: code is null ? null : new Dictionary<string, object?> { ["code"] = code });

    public static IResult Field(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
