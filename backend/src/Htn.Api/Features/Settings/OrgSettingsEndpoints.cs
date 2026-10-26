using System.Globalization;
using System.Security.Cryptography;
using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Domain.Files;
using Htn.Domain.Settings;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Settings;

public static class OrgSettingsEndpoints
{
    public const string PngContentType = "image/png";

    public static RouteGroupBuilder MapOrgSettings(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/settings").WithTags("Settings");
        // Any signed-in user: the app needs the brand color for its theme (also before a Board user has set up 2FA).
        g.MapGet("/org", Get).RequireAuthorization(Policies.SettingsPersonal).AllowWithoutTwoFactor();
        g.MapPut("/points", UpdatePoints).RequireAuthorization(Policies.SettingsManage).Validate<UpdatePointsRequest>();
        g.MapPut("/branding", UpdateBranding).RequireAuthorization(Policies.SettingsManage).Validate<UpdateBrandingRequest>();
        g.MapPost("/branding/logo", UploadLogo).RequireAuthorization(Policies.SettingsManage);
        return g;
    }

    public static string FileUrl(Guid id) => $"/api/files/{id}";

    private static PointsSettingsDto Points(OrgSettings s) => new(
        s.LeadMultiplier, s.CollabMultiplier, s.AssistantMultiplier,
        new PointPresetsDto(s.PresetSmall, s.PresetMedium, s.PresetLarge, s.PresetXl), s.RequireTaskApproval);

    private static BrandingDto Branding(OrgSettings s) => new(
        OrgSettings.SafeBrandColor(s.BrandColor),
        s.LogoLightFileId is { } light ? FileUrl(light) : null,
        s.LogoDarkFileId is { } dark ? FileUrl(dark) : null,
        s.PaymentDeadlineHours);

    private static Task<OrgSettings> Load(HtnDbContext db, CancellationToken ct, bool tracking = true) =>
        (tracking ? db.OrgSettings : db.OrgSettings.AsNoTracking()).SingleAsync(s => s.Id == OrgSettings.SingletonId, ct);

    private static async Task<IResult> Get(HtnDbContext db, CancellationToken ct)
    {
        var s = await Load(db, ct, tracking: false);
        return TypedResults.Ok(new OrgSettingsDto(Points(s), Branding(s)));
    }

    private static async Task<IResult> UpdatePoints(
        UpdatePointsRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var s = await Load(db, ct);
        var before = Points(s);
        var p = req.Presets!;
        s.UpdatePoints(req.LeadMultiplier!.Value, req.CollabMultiplier!.Value, req.AssistantMultiplier!.Value,
            p.Small!.Value, p.Medium!.Value, p.Large!.Value, p.Xl!.Value, req.RequireTaskApproval!.Value,
            clock.GetUtcNow(), me.Id);
        var after = Points(s);
        audit.Record(me.Id, "settings.points_update", nameof(OrgSettings), s.Id.ToString(CultureInfo.InvariantCulture), before, after);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(after);
    }

    private static async Task<IResult> UpdateBranding(
        UpdateBrandingRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        if (!ColorContrast.HasEnoughContrastWithWhite(req.BrandColor))
            return Problems.Field("brandColor",
                $"White text on this color has {ColorContrast.RatioAgainstWhite(req.BrandColor):0.0}:1 contrast. Pick a darker color (at least 4.5:1).",
                "low_contrast");

        var s = await Load(db, ct);
        var before = new { s.BrandColor, s.PaymentDeadlineHours };
        s.UpdateBranding(req.BrandColor, req.PaymentDeadlineHours!.Value, clock.GetUtcNow(), me.Id);
        audit.Record(me.Id, "settings.branding_update", nameof(OrgSettings), s.Id.ToString(CultureInfo.InvariantCulture),
            before, new { s.BrandColor, s.PaymentDeadlineHours });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(Branding(s));
    }

    private static async Task<IResult> UploadLogo(
        HttpRequest request, string? variant, HtnDbContext db, IFileStorage storage, IAuditLog audit, ICurrentUser me,
        TimeProvider clock, CancellationToken ct)
    {
        if (!Enum.TryParse<LogoVariant>(variant, ignoreCase: true, out var v) || !Enum.IsDefined(v) || int.TryParse(variant, out _))
            return Problems.Field("variant", "Choose the light or dark logo.");

        var (file, error) = await Uploads.ReadSingleFileAsync(request, "file", StoredFile.MaxBytes, "The logo must be 1 MB or smaller.", ct);
        if (error is not null) return error;
        // Trust the bytes, not the declared content type or file name.
        if (!FileSignatures.IsPng(file!.Bytes))
            return Problems.Field("file", "Upload a PNG image.", "unsupported_type");
        var (width, height) = FileSignatures.PngSize(file.Bytes);
        if (width == 0 || height == 0)
            return Problems.Field("file", "Upload a PNG image.", "unsupported_type");
        if (width > FileSignatures.MaxImageDimension || height > FileSignatures.MaxImageDimension)
            return Problems.Field("file",
                $"The logo can be at most {FileSignatures.MaxImageDimension}×{FileSignatures.MaxImageDimension} pixels.", "image_too_large");

        var now = clock.GetUtcNow();
        var stored = StoredFile.Create(PngContentType, file.Bytes.LongLength, Convert.ToHexStringLower(SHA256.HashData(file.Bytes)),
            v == LogoVariant.Light ? StoredFilePurposes.LogoLight : StoredFilePurposes.LogoDark, file.FileName, now, me.Id);
        await storage.SaveAsync(stored.Id, file.Bytes, ct);

        var s = await Load(db, ct);
        var before = v == LogoVariant.Light ? s.LogoLightFileId : s.LogoDarkFileId;
        db.StoredFiles.Add(stored);
        s.SetLogo(v, stored.Id, now, me.Id);
        audit.Record(me.Id, "settings.logo_upload", nameof(OrgSettings), s.Id.ToString(CultureInfo.InvariantCulture),
            new { variant = v, fileId = before }, new { variant = v, fileId = stored.Id, stored.SizeBytes, stored.Sha256 });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(Branding(s));
    }
}
