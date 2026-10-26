using System.Net;
using System.Security.Cryptography;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Files;
using Htn.Domain.Organization;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Htn.Api.Tests.Events.EventTestData;

namespace Htn.Api.Tests.Events;

public sealed class EventCoverAndPublicFilesTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    private static readonly byte[] Png = Settings.OrgSettingsEndpointsTests.Png;

    /// <summary>A minimal JPEG (SOI, APP0, SOF0 with the given size, SOS, EOI), optionally padded to a total size.</summary>
    private static byte[] Jpeg(ushort width = 1600, ushort height = 900, bool withSof = true, int padding = 0)
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };
        bytes.AddRange([0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00]);
        if (withSof)
        {
            bytes.AddRange([0xFF, 0xC0, 0x00, 0x11, 0x08, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 0x03,
                0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01]);
        }
        bytes.AddRange([0xFF, 0xDA, 0x00, 0x02]);
        bytes.AddRange(new byte[padding]);
        bytes.AddRange([0xFF, 0xD9]);
        return [.. bytes];
    }

    private static byte[] PngWithSize(uint width, uint height)
    {
        var png = (byte[])Png.Clone();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16, 4), width);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20, 4), height);
        return png;
    }

    private async Task<(ApiClient Board, Guid EventId)> EventAsync()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        return (board, (await CreateAsync(board)).Id());
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpg")]
    public async Task Png_and_jpg_covers_are_accepted_and_served_publicly(string kind)
    {
        var (board, id) = await EventAsync();
        var bytes = kind == "png" ? Png : Jpeg();
        // The declared type is ignored: the bytes decide.
        var res = await board.PostFileAsync($"/api/events/{id}/cover", bytes, "cover.bin", "application/octet-stream");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var detail = await ApiClient.JsonAsync(res);
        var url = detail.GetProperty("coverUrl").GetString()!;
        Assert.StartsWith("/api/public/files/", url, StringComparison.Ordinal);

        var file = await factory.NewClient().GetAsync(url); // anonymous
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal(kind == "png" ? "image/png" : "image/jpeg", file.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", file.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("default-src 'none'", file.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("same-origin", file.Headers.GetValues("Cross-Origin-Resource-Policy").Single());
        Assert.Equal(bytes, await file.Content.ReadAsByteArrayAsync());

        var fileId = Guid.Parse(url["/api/public/files/".Length..]);
        var stored = await factory.DbAsync(db => db.StoredFiles.AsNoTracking().SingleAsync(f => f.Id == fileId));
        Assert.Equal(StoredFilePurposes.EventCover, stored.Purpose);

        var audit = await factory.DbAsync(db => db.AuditEntries.AsNoTracking()
            .SingleAsync(a => a.Action == "event.cover_upload" && a.EntityId == id.ToString()));
        Assert.Contains(stored.Id.ToString(), audit.AfterJson, StringComparison.Ordinal);

        // List shows it too.
        var list = await ApiClient.JsonAsync(await board.GetAsync("/api/events?tab=drafts"));
        Assert.Equal(url, list.EnumerateArray().Single(e => e.Id() == id).GetProperty("coverUrl").GetString());
    }

    [Fact]
    public async Task Covers_over_1_mb_up_to_2_mb_are_accepted()
    {
        var (board, id) = await EventAsync();
        var big = Jpeg(padding: (int)(1.8 * 1024 * 1024));
        Assert.True(big.Length > 1024 * 1024);
        var res = await board.PostFileAsync($"/api/events/{id}/cover", big, "big.jpg", "image/jpeg");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Bad_covers_are_rejected()
    {
        var (board, id) = await EventAsync();
        (byte[] Bytes, string Code)[] cases =
        [
            ("GIF89a\x01\x00\x01\x00"u8.ToArray(), "unsupported_type"),
            ("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray(), "unsupported_type"),
            (Jpeg(withSof: false), "unsupported_type"),
            (Jpeg()[..20], "unsupported_type"),
            (Png[..10], "unsupported_type"),
            (Jpeg(4097, 100), "image_too_large"),
            (Jpeg(100, 4097), "image_too_large"),
            (PngWithSize(5000, 10), "image_too_large"),
            (Jpeg(padding: 2 * 1024 * 1024), "file_too_large"),
        ];
        foreach (var (bytes, code) in cases)
        {
            var res = await board.PostFileAsync($"/api/events/{id}/cover", bytes, "cover.jpg", "image/jpeg");
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            var body = await ApiClient.JsonAsync(res);
            Assert.Equal(code, body.Code());
            Assert.True(body.HasError("file"));
        }

        Assert.Null(await factory.DbAsync(db => db.Events.Where(e => e.Id == id).Select(e => e.CoverFileId).SingleAsync()));
        Assert.False(await factory.DbAsync(db => db.AuditEntries.AnyAsync(a => a.Action == "event.cover_upload" && a.EntityId == id.ToString())));

        var edge = await board.PostFileAsync($"/api/events/{id}/cover", Jpeg(4096, 4096), "max.jpg", "image/jpeg");
        Assert.Equal(HttpStatusCode.OK, edge.StatusCode);
        Assert.NotNull(await factory.DbAsync(db => db.Events.Where(e => e.Id == id).Select(e => e.CoverFileId).SingleAsync()));
        Assert.Equal(HttpStatusCode.NotFound,
            (await board.PostFileAsync($"/api/events/{Guid.NewGuid()}/cover", Png, "c.png", "image/png")).StatusCode);
    }

    private async Task<Guid> StoreFileAsync(string purpose)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HtnDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var file = StoredFile.Create("image/png", Png.Length, Convert.ToHexStringLower(SHA256.HashData(Png)), purpose, "x.png", DateTimeOffset.UtcNow, null);
        await storage.SaveAsync(file.Id, Png, CancellationToken.None);
        db.StoredFiles.Add(file);
        await db.SaveChangesAsync();
        return file.Id;
    }

    [Fact]
    public async Task Public_files_serve_only_logos_and_covers()
    {
        var logoLight = await StoreFileAsync(StoredFilePurposes.LogoLight);
        var logoDark = await StoreFileAsync(StoredFilePurposes.LogoDark);
        var cover = await StoreFileAsync(StoredFilePurposes.EventCover);
        var proof = await StoreFileAsync("payment_proof");
        var receipt = await StoreFileAsync("receipt");

        var anonymous = factory.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/public/files/{logoLight}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/public/files/{logoDark}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/public/files/{cover}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/public/files/{proof}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/public/files/{receipt}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/public/files/{Guid.NewGuid()}")).StatusCode);

        // /api/files keeps its Phase 2 rules: signed in, logos only.
        var goon = await factory.SignedInAsync(PersonLevel.Goon);
        Assert.Equal(HttpStatusCode.NotFound, (await goon.GetAsync($"/api/files/{cover}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await goon.GetAsync($"/api/files/{logoDark}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/files/{logoDark}")).StatusCode);

        // Signed-in users (even a Board user who hasn't set up 2FA) can load public files.
        Assert.Equal(HttpStatusCode.OK, (await goon.GetAsync($"/api/public/files/{cover}")).StatusCode);
    }

    [Fact]
    public async Task Public_files_are_open_to_board_without_2fa()
    {
        var strict = new Board2faApiFactory();
        await strict.InitializeAsync();
        try
        {
            var board = await strict.SignedInAsync(PersonLevel.Board);
            var res = await board.GetAsync($"/api/public/files/{Guid.NewGuid()}");
            Assert.Equal(HttpStatusCode.NotFound, res.StatusCode); // reached the handler, not the 2FA gate
        }
        finally
        {
            await ((IAsyncLifetime)strict).DisposeAsync();
        }
    }
}
