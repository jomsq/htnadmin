using FluentValidation;

namespace Htn.Api.Common;

/// <summary>Runs the FluentValidation validator for <typeparamref name="T"/> and returns 400 ValidationProblem on failure.</summary>
public sealed class ValidationFilter<T>(IValidator<T> validator) : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var arg = context.Arguments.OfType<T>().FirstOrDefault();
        if (arg is null)
            return Problems.Result(StatusCodes.Status400BadRequest, "Request body is required.");
        var result = await validator.ValidateAsync(arg, context.HttpContext.RequestAborted);
        if (!result.IsValid)
            return TypedResults.ValidationProblem(result.ToDictionary());
        return await next(context);
    }
}

public static class ValidationExtensions
{
    public static RouteHandlerBuilder Validate<T>(this RouteHandlerBuilder builder) where T : class =>
        builder.AddEndpointFilter<ValidationFilter<T>>().ProducesValidationProblem();

    /// <summary>camelCase keys in ValidationProblem errors, matching the JSON request property names.</summary>
    public static void UseCamelCasePropertyNames()
    {
        ValidatorOptions.Global.PropertyNameResolver = (_, member, _) =>
            member is null ? null : System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(member.Name);
    }
}
