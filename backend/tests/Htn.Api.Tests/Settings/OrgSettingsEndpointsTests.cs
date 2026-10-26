using System.Net;
using System.Text.Json;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Htn.Api.Tests.Settings;

public sealed class OrgSettingsEndpointsTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    /// <summary>A real 1×1 PNG.</summary>
    public static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static object Points(decimal lead = 1.0m, decimal collab = 0.7m, decimal assistant = 0.3m,
        int small = 10, int medium = 20, int large = 40, int xl = 80, bool approval = true) => new
    {
        leadMultiplier = lead,
        collabMultiplier = collab,
        assistantMultiplier = assistant,
        presets = new { small, medium, large, xl },
        requireTaskApproval = approval,
    };

    [Fact]
    public async Task Any_signed_in_user_reads_org_settings_with_defaults()
    {
        var apprentice = await factory.SignedInAsync(PersonLevel.Apprentice);
        var res = await apprentice.GetAsync("/api/settings/org");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var s = await ApiClient.JsonAsync(res);
        var branding = s.GetProperty("branding");
        Assert.Matches("^#[0-9A-F]{6}$", branding.GetProperty("brandColor").GetString());
        Assert.True(branding.TryGetProperty("logoLightUrl", out _));
        Assert.True(branding.TryGetProperty("paymentDeadlineHours", out _));
        var presets = s.GetProperty("points").GetProperty("presets");
        Assert.True(presets.GetProperty("small").GetInt32() < presets.GetProperty("xl").GetInt32());

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().GetAsync("/api/settings/org")).StatusCode);
    }

    [Fact]
    public async Task Seeded_defaults_match_the_domain()
    {
        // A fresh row read straight from the migration seed.
        var row = await factory.DbAsync(db => db.OrgSettings.AsNoTracking().SingleAsync());
        Assert.Equal(1.0m, row.LeadMultiplier);
        Assert.Equal(0.7m, row.CollabMultiplier);
        Assert.Equal(0.3m, row.AssistantMultiplier);
        Assert.Equal((10, 20, 40, 80), (row.PresetSmall, row.PresetMedium, row.PresetLarge, row.PresetXl));
        Assert.True(row.RequireTaskApproval);
        Assert.Equal(48, row.PaymentDeadlineHours);
    }

    [Fact]
    public async Task Goon_and_apprentice_cannot_change_settings()
    {
        foreach (var level in new[] { PersonLevel.Goon, PersonLevel.Apprentice })
        {
            var client = await factory.SignedInAsync(level);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsync("/api/settings/points", Points())).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsync("/api/settings/branding", new { brandColor = "#0D47A1", paymentDeadlineHours = 48 })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostFileAsync("/api/settings/branding/logo?variant=light", Png, "logo.png", "image/png")).StatusCode);
        }
    }

    [Fact]
    public async Task Points_update_validates_and_audits()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var res = await board.PutAsync("/api/settings/points", Points(lead: 1.2m, collab: 0.5m, assistant: 0.1m, small: 5, medium: 15, large: 30, xl: 60, approval: false));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var points = await ApiClient.JsonAsync(res);
        Assert.Equal(1.2m, points.GetProperty("leadMultiplier").GetDecimal());
        Assert.Equal(60, points.GetProperty("presets").GetProperty("xl").GetInt32());
        Assert.False(points.GetProperty("requireTaskApproval").GetBoolean());

        var org = await ApiClient.JsonAsync(await board.GetAsync("/api/settings/org"));
        Assert.Equal(0.5m, org.GetProperty("points").GetProperty("collabMultiplier").GetDecimal());

        var audit = (await factory.DbAsync(db => db.AuditEntries.AsNoTracking().Where(a => a.Action == "settings.points_update").ToListAsync()))[^1];
        Assert.Equal(1.2m, JsonDocument.Parse(audit.AfterJson!).RootElement.GetProperty("leadMultiplier").GetDecimal());
        Assert.NotNull(audit.BeforeJson);

        var bad = await board.PutAsync("/api/settings/points", Points(lead: 2.5m, collab: 0.75m, small: 20, medium: 20, xl: 1001));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var errors = (await ApiClient.JsonAsync(bad)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("leadMultiplier", out _));
        Assert.True(errors.TryGetProperty("collabMultiplier", out _));
        Assert.True(errors.TryGetProperty("presets.medium", out _));
        Assert.True(errors.TryGetProperty("presets.xl", out _));

        var missing = await board.PutAsync("/api/settings/points", new { leadMultiplier = 1.0m });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await board.PutAsync("/api/settings/points", Points())).StatusCode); // reset
    }

    [Fact]
    public async Task Low_contrast_brand_color_is_rejected()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var res = await board.PutAsync("/api/settings/branding", new { brandColor = "#FFEB3B", paymentDeadlineHours = 48 });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await ApiClient.JsonAsync(res);
        Assert.Equal("low_contrast", body.GetProperty("code").GetString());
        Assert.True(body.GetProperty("errors").TryGetProperty("brandColor", out _));

        var notHex = await board.PutAsync("/api/settings/branding", new { brandColor = "red;}", paymentDeadlineHours = 48 });
        Assert.Equal(HttpStatusCode.BadRequest, notHex.StatusCode);
        Assert.True((await ApiClient.JsonAsync(notHex)).GetProperty("errors").TryGetProperty("brandColor", out _));

        var hours = await board.PutAsync("/api/settings/branding", new { brandColor = "#0D47A1", paymentDeadlineHours = 337 });
        Assert.True((await ApiClient.JsonAsync(hours)).GetProperty("errors").TryGetProperty("paymentDeadlineHours", out _));
    }

    [Fact]
    public async Task Branding_update_reaches_invite_and_reset_emails()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var res = await board.PutAsync("/api/settings/branding", new { brandColor = "#0d47a1", paymentDeadlineHours = 72 });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var branding = await ApiClient.JsonAsync(res);
        Assert.Equal("#0D47A1", branding.GetProperty("brandColor").GetString());
        Assert.Equal(72, branding.GetProperty("paymentDeadlineHours").GetInt32());
        Assert.True(await factory.DbAsync(db => db.AuditEntries.AnyAsync(a => a.Action == "settings.branding_update")));

        var email = HtnApiFactory.UniqueEmail("brand");
        await board.PostAsync("/api/people", new { firstName = "Bea", lastName = "Uy", email, level = "Goon" });
        var goon = await factory.CreateUserAsync(PersonLevel.Goon);
        await board.PostAsync($"/api/people/{goon.PersonId}/send-password-reset");
        await factory.ProcessOutboxAsync();
        var invite = factory.Emails.To(email).Single();
        Assert.Contains("#0D47A1", invite.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("#B71C1C", invite.Html, StringComparison.Ordinal);
        Assert.Contains("#0D47A1", factory.Emails.To(goon.Email).Single().Html, StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.OK, (await board.PutAsync("/api/settings/branding", new { brandColor = "#B71C1C", paymentDeadlineHours = 48 })).StatusCode);
    }

    [Fact]
    public async Task Png_logo_upload_is_served_with_safe_headers()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var res = await board.PostFileAsync("/api/settings/branding/logo?variant=dark", Png, "logo.png", "image/png");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var branding = await ApiClient.JsonAsync(res);
        var url = branding.GetProperty("logoDarkUrl").GetString()!;
        Assert.StartsWith("/api/files/", url, StringComparison.Ordinal);

        var apprentice = await factory.SignedInAsync(PersonLevel.Apprentice);
        var file = await apprentice.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal("image/png", file.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", file.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("default-src 'none'", file.Headers.GetValues("Content-Security-Policy").Single());
        // "private" normally; the cookie handler swaps in "no-store" when it re-issues the auth cookie on the same
        // response (every request in tests, where the stamp interval is 0). Either way shared caches never keep it.
        var cache = file.Headers.CacheControl;
        Assert.True(cache is { Private: true } or { NoStore: true }, "Cache-Control: " + cache);
        Assert.False(cache?.Public ?? false);
        Assert.Equal(Png, await file.Content.ReadAsByteArrayAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await apprentice.GetAsync($"/api/files/{Guid.NewGuid()}")).StatusCode);

        var org = await ApiClient.JsonAsync(await apprentice.GetAsync("/api/settings/org"));
        Assert.Equal(url, org.GetProperty("branding").GetProperty("logoDarkUrl").GetString());

        var audit = (await factory.DbAsync(db => db.AuditEntries.AsNoTracking().Where(a => a.Action == "settings.logo_upload").ToListAsync()))[^1];
        Assert.Equal(url["/api/files/".Length..], JsonDocument.Parse(audit.AfterJson!).RootElement.GetProperty("fileId").GetString());

        // Uploading again replaces it (the old file is kept, never deleted).
        var again = await ApiClient.JsonAsync(await board.PostFileAsync("/api/settings/branding/logo?variant=dark", Png, "logo2.png", "image/png"));
        Assert.NotEqual(url, again.GetProperty("logoDarkUrl").GetString());
        Assert.Equal(HttpStatusCode.OK, (await apprentice.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Non_png_bytes_are_rejected_even_when_labelled_png()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"/>"u8.ToArray();
        var res = await board.PostFileAsync("/api/settings/branding/logo?variant=light", svg, "logo.png", "image/png");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await ApiClient.JsonAsync(res);
        Assert.Equal("unsupported_type", body.GetProperty("code").GetString());
        Assert.True(body.GetProperty("errors").TryGetProperty("file", out _));

        var truncated = await board.PostFileAsync("/api/settings/branding/logo?variant=light", Png[..10], "logo.png", "image/png");
        Assert.Equal(HttpStatusCode.BadRequest, truncated.StatusCode);

        var big = new byte[1024 * 1024 + 1];
        Png.CopyTo(big, 0);
        var tooBig = await board.PostFileAsync("/api/settings/branding/logo?variant=light", big, "big.png", "image/png");
        Assert.Equal(HttpStatusCode.BadRequest, tooBig.StatusCode);
        Assert.Equal("file_too_large", (await ApiClient.JsonAsync(tooBig)).GetProperty("code").GetString());

        var badVariant = await board.PostFileAsync("/api/settings/branding/logo?variant=sepia", Png, "logo.png", "image/png");
        Assert.True((await ApiClient.JsonAsync(badVariant)).GetProperty("errors").TryGetProperty("variant", out _));

        Assert.Equal(0, await factory.DbAsync(db => db.StoredFiles.CountAsync(f => f.Purpose == "logo_light")));
        Assert.Null(await factory.DbAsync(db => db.OrgSettings.Select(s => s.LogoLightFileId).SingleAsync()));
    }

    [Fact]
    public async Task Board_without_2fa_can_still_read_the_theme()
    {
        var strict = new Board2faApiFactory();
        await strict.InitializeAsync();
        try
        {
            var board = await strict.SignedInAsync(PersonLevel.Board);
            Assert.Equal(HttpStatusCode.OK, (await board.GetAsync("/api/settings/org")).StatusCode);
            var gated = await board.PutAsync("/api/settings/points", Points());
            Assert.Equal(HttpStatusCode.Forbidden, gated.StatusCode);
            Assert.Equal("2fa_required", (await ApiClient.JsonAsync(gated)).GetProperty("code").GetString());
        }
        finally
        {
            await ((IAsyncLifetime)strict).DisposeAsync();
        }
    }

    private static byte[] PngWithSize(uint width, uint height)
    {
        var png = (byte[])Png.Clone();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16, 4), width);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20, 4), height);
        return png;
    }

    [Theory]
    [InlineData(4097u, 1u)]
    [InlineData(1u, 5000u)]
    public async Task Png_larger_than_4096_pixels_is_rejected(uint width, uint height)
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var res = await board.PostFileAsync("/api/settings/branding/logo?variant=light", PngWithSize(width, height), "huge.png", "image/png");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("image_too_large", (await ApiClient.JsonAsync(res)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Png_of_exactly_4096_pixels_is_accepted()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var res = await board.PostFileAsync("/api/settings/branding/logo?variant=dark", PngWithSize(4096, 4096), "max.png", "image/png");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Theory]
    [InlineData("#0D47A1\n")]
    [InlineData("#0D47A1\r\n")]
    [InlineData(" #0D47A1")]
    public async Task Brand_color_with_trailing_or_leading_junk_is_rejected(string color)
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var res = await board.PutAsync("/api/settings/branding", new { brandColor = color, paymentDeadlineHours = 48 });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.True((await ApiClient.JsonAsync(res)).GetProperty("errors").TryGetProperty("brandColor", out _));
    }

    private async Task<Guid> StoreFileAsync(HtnApiFactory host, string purpose)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Htn.Infrastructure.Data.HtnDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<Htn.Infrastructure.Storage.IFileStorage>();
        var file = Htn.Domain.Files.StoredFile.Create("image/png", Png.Length,
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Png)), purpose, "x.png", DateTimeOffset.UtcNow, null);
        await storage.SaveAsync(file.Id, Png, CancellationToken.None);
        db.StoredFiles.Add(file);
        await db.SaveChangesAsync();
        return file.Id;
    }

    [Fact]
    public async Task Files_endpoint_serves_logos_only()
    {
        var other = await StoreFileAsync(factory, "receipt");
        var logo = await StoreFileAsync(factory, "logo_dark"); // not logo_light: another test counts those
        var goon = await factory.SignedInAsync(PersonLevel.Goon);
        Assert.Equal(HttpStatusCode.NotFound, (await goon.GetAsync($"/api/files/{other}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await goon.GetAsync($"/api/files/{logo}")).StatusCode);
    }

    [Fact]
    public async Task Board_without_2fa_can_still_load_logo_files()
    {
        var strict = new Board2faApiFactory();
        await strict.InitializeAsync();
        try
        {
            var logo = await StoreFileAsync(strict, "logo_dark");
            var board = await strict.SignedInAsync(PersonLevel.Board);
            Assert.Equal(HttpStatusCode.OK, (await board.GetAsync($"/api/files/{logo}")).StatusCode);
        }
        finally
        {
            await ((IAsyncLifetime)strict).DisposeAsync();
        }
    }
}
