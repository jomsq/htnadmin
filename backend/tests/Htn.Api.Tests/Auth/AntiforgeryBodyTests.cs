using System.Net;
using System.Net.Http.Headers;
using Htn.Api.Hosting;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;

namespace Htn.Api.Tests.Auth;

public sealed class AntiforgeryBodyTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    /// <summary>
    /// A multipart body that sends its first bytes and then stalls until released. If the server tried to read the
    /// form (as antiforgery's form-token lookup would), the request could never complete.
    /// </summary>
    private sealed class StallingMultipartContent : HttpContent
    {
        public const string Boundary = "htn-test-boundary";
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public StallingMultipartContent() =>
            Headers.ContentType = MediaTypeHeaderValue.Parse($"multipart/form-data; boundary={Boundary}");

        public bool ReachedStall { get; private set; }
        public bool FinishedBody { get; private set; }

        public void Release() => _release.TrySetResult();

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var head = System.Text.Encoding.ASCII.GetBytes(
                $"--{Boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"r.csv\"\r\nContent-Type: text/csv\r\n\r\n");
            await stream.WriteAsync(head);
            await stream.FlushAsync();
            ReachedStall = true;
            await _release.Task;
            FinishedBody = true;
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false; // chunked: the server can't know the size up front
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Multipart_post_without_csrf_header_is_rejected_before_the_body_is_read(string? header)
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var content = new StallingMultipartContent();
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/people/import?dryRun=true") { Content = content };
        if (header is not null) req.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", header);

        try
        {
            var res = await board.Http.SendAsync(req).WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            Assert.Equal("antiforgery", (await ApiClient.JsonAsync(res)).GetProperty("code").GetString());
            Assert.False(content.FinishedBody);
        }
        finally
        {
            content.Release();
        }
    }

    [Fact]
    public void Global_body_limit_fits_a_1_mb_upload_plus_overhead()
    {
        Assert.Equal(2 * 1024 * 1024, HostingLimits.MaxRequestBodyBytes);
        Assert.True(HostingLimits.MaxRequestBodyBytes > Htn.Api.Features.Organization.PeopleImport.MaxBytes + 64 * 1024);
        Assert.True(HostingLimits.MaxRequestBodyBytes > Htn.Domain.Files.StoredFile.MaxBytes + 64 * 1024);
    }
}
