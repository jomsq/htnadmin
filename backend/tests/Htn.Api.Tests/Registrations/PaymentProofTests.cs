using System.Net;
using System.Net.Http.Headers;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Files;
using Htn.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.Events.EventTestData;
using static Htn.Api.Tests.Registrations.RegistrationTestData;

namespace Htn.Api.Tests.Registrations;

public sealed class PaymentProofTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    private static async Task<HttpResponseMessage> UploadAsync(ApiClient client, string token, byte[] file, string? reference = "GCash 7741 0923",
        string contentType = "image/png")
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/public/proof/{token}");
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(content, "file", "screenshot.png");
        if (reference is not null) form.Add(new StringContent(reference), "reference");
        req.Content = form;
        req.Headers.Add("X-CSRF-TOKEN", await client.CsrfTokenAsync());
        return await client.Http.SendAsync(req);
    }

    private async Task<(OnSaleEvent Ev, string Email, string Pid, Guid RegId, string Token)> RegisterOneAsync(ApiClient board, OnSaleEvent? ev = null)
    {
        ev ??= await PublishedAsync(board);
        var email = HtnApiFactory.UniqueEmail("proof");
        var created = await RegisterOkAsync(factory.NewClient(), ev.Slug, Body(ev.TicketId, [Person(email)]));
        var pid = created.GetProperty("participantIds")[0].GetString()!;
        await factory.DrainOutboxAsync();
        return (ev, email, pid, await RegistrationIdAsync(factory, pid), factory.Emails.ProofToken(email));
    }

    [Fact]
    public async Task Registrant_uploads_proof_and_admin_sees_it_but_the_public_never_does()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var (ev, _, pid, regId, token) = await RegisterOneAsync(board);
        var anon = factory.NewClient();

        var info = await anon.GetAsync($"/api/public/proof/{token}");
        Assert.Equal(HttpStatusCode.OK, info.StatusCode);
        Assert.Equal("no-store", info.Headers.CacheControl?.ToString());
        var body = await ApiClient.JsonAsync(info);
        Assert.Equal(pid, body.GetProperty("participantId").GetString());
        Assert.Equal(1500m, body.GetProperty("amountDue").GetDecimal());
        Assert.False(body.GetProperty("uploaded").GetBoolean());
        Assert.False(body.TryGetProperty("email", out _));

        Assert.Equal(HttpStatusCode.NoContent, (await UploadAsync(anon, token, Png)).StatusCode);
        Assert.True((await ApiClient.JsonAsync(await anon.GetAsync($"/api/public/proof/{token}"))).GetProperty("uploaded").GetBoolean());

        var proof = await factory.DbAsync(db => db.PaymentProofs.AsNoTracking().SingleAsync(p => p.RegistrationId == regId));
        Assert.Equal("GCash 7741 0923", proof.Reference);
        var file = await factory.DbAsync(db => db.StoredFiles.AsNoTracking().SingleAsync(f => f.Id == proof.FileId));
        Assert.Equal(StoredFilePurposes.PaymentProof, file.Purpose);
        Assert.Null(file.OriginalName);
        var audit = await factory.DbAsync(db => db.AuditEntries.AsNoTracking().SingleAsync(a => a.Action == "registration.proof_uploaded" && a.EntityId == regId.ToString()));
        Assert.Contains(file.Sha256, audit.AfterJson, StringComparison.Ordinal);

        // Admin: row and detail show it, and the image streams with locked-down headers.
        var row = (await ApiClient.JsonAsync(await board.GetAsync($"/api/events/{ev.Id}/registrations"))).GetProperty("items")[0];
        Assert.True(row.GetProperty("hasProof").GetBoolean());
        Assert.Equal("GCash 7741 0923", row.GetProperty("reference").GetString());
        var detail = await ApiClient.JsonAsync(await board.GetAsync($"/api/events/{ev.Id}/registrations/{regId}"));
        var url = detail.GetProperty("proof").GetProperty("url").GetString()!;
        var image = await board.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", image.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("default-src 'none'", image.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal(Png, await image.Content.ReadAsByteArrayAsync());

        // Never through the public or member file endpoints.
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/api/public/files/{file.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await board.GetAsync($"/api/files/{file.Id}")).StatusCode);
        // Not for other roles, or anonymous.
        var goon = await factory.SignedInAsync(PersonLevel.Goon);
        Assert.Equal(HttpStatusCode.Forbidden, (await goon.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task A_token_only_reaches_its_own_registration()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var a = await RegisterOneAsync(board);
        var b = await RegisterOneAsync(board, a.Ev);
        var anon = factory.NewClient();

        Assert.Equal(a.Pid, (await ApiClient.JsonAsync(await anon.GetAsync($"/api/public/proof/{a.Token}"))).GetProperty("participantId").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await UploadAsync(anon, a.Token, Png)).StatusCode);
        Assert.Equal(1, await factory.DbAsync(db => db.PaymentProofs.CountAsync(p => p.RegistrationId == a.RegId)));
        Assert.Equal(0, await factory.DbAsync(db => db.PaymentProofs.CountAsync(p => p.RegistrationId == b.RegId)));

        foreach (var bad in new[] { "nope", a.Token + "x", a.Token.ToUpperInvariant(), new string('a', 43), a.RegId.ToString(), a.Pid })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/api/public/proof/{Uri.EscapeDataString(bad)}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await UploadAsync(anon, Uri.EscapeDataString(bad), Png)).StatusCode);
        }
    }

    [Fact]
    public async Task Uploads_must_be_png_or_jpg_and_pending()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var r = await RegisterOneAsync(board);
        var anon = factory.NewClient();

        var notImage = await UploadAsync(anon, r.Token, "%PDF-1.7 not an image"u8.ToArray(), contentType: "image/png");
        Assert.Equal(HttpStatusCode.BadRequest, notImage.StatusCode);
        Assert.Equal("unsupported_type", (await ApiClient.JsonAsync(notImage)).Code());
        var longRef = await UploadAsync(anon, r.Token, Png, reference: new string('x', 61));
        Assert.True((await ApiClient.JsonAsync(longRef)).HasError("reference"));
        var tooBig = await UploadAsync(anon, r.Token, [.. Png, .. new byte[5 * 1024 * 1024]]);
        Assert.Equal(HttpStatusCode.BadRequest, tooBig.StatusCode);
        Assert.Equal("file_too_large", (await ApiClient.JsonAsync(tooBig)).Code());
        Assert.Equal(0, await factory.DbAsync(db => db.PaymentProofs.CountAsync(p => p.RegistrationId == r.RegId)));

        // Just under 5 MB is fine.
        Assert.Equal(HttpStatusCode.NoContent, (await UploadAsync(anon, r.Token, [.. Png, .. new byte[5 * 1024 * 1024 - Png.Length - 1]])).StatusCode);

        // At most 10 per registration.
        for (var i = 1; i < 10; i++)
            Assert.Equal(HttpStatusCode.NoContent, (await UploadAsync(anon, r.Token, Png)).StatusCode);
        var tooMany = await UploadAsync(anon, r.Token, Png);
        Assert.Equal(HttpStatusCode.Conflict, tooMany.StatusCode);
        Assert.Equal("too_many_uploads", (await ApiClient.JsonAsync(tooMany)).Code());

        // Once paid, the page still opens but uploads stop.
        var paid = await board.PostAsync($"/api/events/{r.Ev.Id}/registrations/mark-paid", new { ids = new[] { r.RegId }, method = "Cash" });
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        Assert.Equal("Paid", (await ApiClient.JsonAsync(await anon.GetAsync($"/api/public/proof/{r.Token}"))).GetProperty("status").GetString());
        var late = await UploadAsync(anon, r.Token, Png);
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        Assert.Equal("not_pending", (await ApiClient.JsonAsync(late)).Code());
    }

    [Fact]
    public async Task Token_expires_when_the_event_ends_and_when_cancelled_or_replaced()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var anon = factory.NewClient();

        var ended = await RegisterOneAsync(board);
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync($"/api/public/proof/{ended.Token}")).StatusCode);
        await factory.DbAsync(db => db.Events.Where(e => e.Id == ended.Ev.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(e => e.StartsAt, DateTimeOffset.UtcNow.AddDays(-2)).SetProperty(e => e.EndsAt, DateTimeOffset.UtcNow.AddMinutes(-1))));
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/api/public/proof/{ended.Token}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await UploadAsync(anon, ended.Token, Png)).StatusCode);

        var resent = await RegisterOneAsync(board);
        Assert.Equal(HttpStatusCode.NoContent, (await board.PostAsync($"/api/events/{resent.Ev.Id}/registrations/{resent.RegId}/resend-billing")).StatusCode);
        await factory.DrainOutboxAsync();
        var newToken = factory.Emails.ProofToken(resent.Email);
        Assert.NotEqual(resent.Token, newToken);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/api/public/proof/{resent.Token}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync($"/api/public/proof/{newToken}")).StatusCode);

        var cancelled = await RegisterOneAsync(board);
        Assert.Equal(HttpStatusCode.OK, (await board.PostAsync($"/api/events/{cancelled.Ev.Id}/registrations/{cancelled.RegId}/cancel", new { reason = "Duplicate" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/api/public/proof/{cancelled.Token}")).StatusCode);
    }
}
