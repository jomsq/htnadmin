using Htn.Domain.Files;
using Htn.Domain.Settings;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Email.Templates;
using Htn.Infrastructure.Storage;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Htn.Infrastructure.Email;

/// <summary>Renders Razor component templates to HTML with <see cref="HtmlRenderer"/>. Brand color comes from org settings.</summary>
public sealed class EmailRenderer(IServiceProvider services, ILoggerFactory loggerFactory, HtnDbContext db, IFileStorage storage)
{
    public const string LogoContentId = "htn-logo";

    /// <summary>
    /// The organization's light logo as an inline attachment (<c>cid:htn-logo</c>), or null when none is uploaded or the
    /// file is missing. Only PNG/JPG logo purposes are ever read here.
    /// </summary>
    public async Task<EmailAttachment?> LogoAsync()
    {
        var fileId = await db.OrgSettings.AsNoTracking().Where(s => s.Id == OrgSettings.SingletonId).Select(s => s.LogoLightFileId).SingleOrDefaultAsync();
        if (fileId is not { } id) return null;
        var meta = await db.StoredFiles.AsNoTracking()
            .SingleOrDefaultAsync(f => f.Id == id && (f.Purpose == StoredFilePurposes.LogoLight || f.Purpose == StoredFilePurposes.LogoDark));
        if (meta is null || meta.ContentType is not (FileSignatures.PngContentType or FileSignatures.JpegContentType)) return null;
        await using var stream = await storage.OpenReadAsync(id, CancellationToken.None);
        if (stream is null) return null;
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        var ext = meta.ContentType == FileSignatures.PngContentType ? "png" : "jpg";
        return new EmailAttachment($"logo.{ext}", meta.ContentType, buffer.ToArray(), LogoContentId);
    }

    /// <summary>The org brand color, re-validated so a bad stored value can't inject CSS; falls back to <see cref="Brand.Color"/>.</summary>
    public async Task<string> BrandColorAsync()
    {
        var stored = await db.OrgSettings.AsNoTracking().Where(s => s.Id == OrgSettings.SingletonId).Select(s => s.BrandColor).SingleOrDefaultAsync();
        return OrgSettings.SafeBrandColor(stored);
    }

    public async Task<string> RenderAsync<TComponent>(IDictionary<string, object?> parameters) where TComponent : IComponent
    {
        await using var renderer = new HtmlRenderer(services, loggerFactory);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters));
            return output.ToHtmlString();
        });
    }

    public async Task<EmailMessage> InviteAsync(InviteEmailPayload p, string link, int expiresInHours)
    {
        var logo = await LogoAsync();
        var html = await RenderAsync<InviteEmail>(new Dictionary<string, object?>
        {
            [nameof(InviteEmail.FirstName)] = p.FirstName,
            [nameof(InviteEmail.InviterName)] = p.InviterName,
            [nameof(InviteEmail.Level)] = p.Level,
            [nameof(InviteEmail.Position)] = p.Position,
            [nameof(InviteEmail.Link)] = link,
            [nameof(InviteEmail.ExpiresInHours)] = expiresInHours,
            [nameof(InviteEmail.BrandColor)] = await BrandColorAsync(),
            [nameof(InviteEmail.LogoCid)] = logo?.ContentId,
        });
        var text =
            $"""
            Hi {p.FirstName},

            {p.InviterName ?? "An admin"} created {EmailText.WithArticle(p.Level)} account for you{(p.Position is null ? "" : $" ({p.Position})")} on the {Brand.OrgName} Admin system.

            Set your password to activate it. The link works once and expires in {expiresInHours} hours:
            {link}

            {Brand.OrgName}
            """;
        return new EmailMessage(p.To, p.FirstName, $"You're invited to {Brand.OrgName} Admin", html, text, EmailCategories.Invite,
            logo is null ? null : [logo]);
    }

    public async Task<EmailMessage> PasswordResetAsync(PasswordResetEmailPayload p, string link, int expiresInMinutes)
    {
        var logo = await LogoAsync();
        var html = await RenderAsync<PasswordResetEmail>(new Dictionary<string, object?>
        {
            [nameof(PasswordResetEmail.FirstName)] = p.FirstName,
            [nameof(PasswordResetEmail.Link)] = link,
            [nameof(PasswordResetEmail.ExpiresInMinutes)] = expiresInMinutes,
            [nameof(PasswordResetEmail.BrandColor)] = await BrandColorAsync(),
            [nameof(PasswordResetEmail.LogoCid)] = logo?.ContentId,
        });
        var text =
            $"""
            Hi {p.FirstName},

            We received a request to reset the password for your {Brand.OrgName} Admin account.
            The link works once and expires in {expiresInMinutes} minutes:
            {link}

            If you didn't ask for this, you can ignore this email; your password stays the same.

            {Brand.OrgName}
            """;
        return new EmailMessage(p.To, p.FirstName, "Reset your password", html, text, EmailCategories.PasswordReset,
            logo is null ? null : [logo]);
    }
}
