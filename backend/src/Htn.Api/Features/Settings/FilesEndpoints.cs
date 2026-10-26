using Htn.Api.Auth;
using Htn.Domain.Files;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Htn.Api.Features.Settings;

public static class FilesEndpoints
{
    public static RouteGroupBuilder MapFiles(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/files").WithTags("Files");
        // Any signed-in user, for logos only (the theme needs them, also before a Board user has set up 2FA).
        g.MapGet("/{id:guid}", Get).RequireAuthorization(Policies.SettingsPersonal).AllowWithoutTwoFactor();
        return g;
    }

    /// <summary>
    /// Anonymous downloads for the public registration page and emails: logos and event covers only. Every other
    /// purpose (payment proofs, documents…) is a 404 here, the same as an unknown ID.
    /// </summary>
    public static RouteGroupBuilder MapPublicFiles(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/public/files").WithTags("Files");
        g.MapGet("/{id:guid}", GetPublic).AllowAnonymous().AllowWithoutTwoFactor();
        return g;
    }

    public static string PublicFileUrl(Guid id) => $"/api/public/files/{id}";

    private static Task<IResult> Get(Guid id, HttpContext http, HtnDbContext db, IFileStorage storage, CancellationToken ct) =>
        // Only logo files are served here; anything else stored later needs its own endpoint and policy.
        Serve(id, StoredFilePurposes.PublicToMembers, http, db, storage, ct);

    private static Task<IResult> GetPublic(Guid id, HttpContext http, HtnDbContext db, IFileStorage storage, CancellationToken ct) =>
        Serve(id, StoredFilePurposes.PublicToEveryone, http, db, storage, ct);

    private static async Task<IResult> Serve(
        Guid id, IReadOnlyList<string> purposes, HttpContext http, HtnDbContext db, IFileStorage storage, CancellationToken ct)
    {
        var meta = await db.StoredFiles.AsNoTracking()
            .SingleOrDefaultAsync(f => f.Id == id && purposes.Contains(f.Purpose), ct);
        if (meta is null) return TypedResults.NotFound();
        var stream = await storage.OpenReadAsync(id, ct);
        if (stream is null) return TypedResults.NotFound();

        var headers = http.Response.Headers;
        headers[HeaderNames.XContentTypeOptions] = "nosniff";
        headers[HeaderNames.ContentSecurityPolicy] = "default-src 'none'";
        headers[HeaderNames.CacheControl] = "private, max-age=86400";
        headers[HeaderNames.ContentDisposition] = "inline";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
        return TypedResults.Stream(stream, meta.ContentType, entityTag: new EntityTagHeaderValue($"\"{meta.Sha256}\""));
    }
}
