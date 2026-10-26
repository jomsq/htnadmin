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

    private static async Task<IResult> Get(Guid id, HttpContext http, HtnDbContext db, IFileStorage storage, CancellationToken ct)
    {
        // Only logo files are served here; anything else stored later needs its own endpoint and policy.
        var meta = await db.StoredFiles.AsNoTracking()
            .SingleOrDefaultAsync(f => f.Id == id && StoredFilePurposes.PublicToMembers.Contains(f.Purpose), ct);
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
