using Microsoft.AspNetCore.Http.Features;

namespace Htn.Api.Common;

public sealed record UploadedFile(byte[] Bytes, string? FileName, string? ContentType);

/// <summary>
/// Reads one file from a multipart form with a hard size limit. Handlers read the form themselves (instead of binding
/// <see cref="IFormFile"/>) so the endpoint keeps going through the app's X-CSRF-TOKEN check.
/// </summary>
public static class Uploads
{
    private const long MultipartOverhead = 64 * 1024;

    public static async Task<(UploadedFile? File, IResult? Error)> ReadSingleFileAsync(
        HttpRequest request, string field, long maxBytes, string tooLargeMessage, CancellationToken ct)
    {
        if (!request.HasFormContentType)
            return (null, Problems.Field(field, "Upload a file.", "file_required"));
        if (request.ContentLength is { } length && length > maxBytes + MultipartOverhead)
            return (null, Problems.Field(field, tooLargeMessage, "file_too_large"));

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(new FormOptions
            {
                MultipartBodyLengthLimit = maxBytes + 1,
                ValueCountLimit = 16,
                MultipartHeadersLengthLimit = 16 * 1024,
                BufferBodyLengthLimit = maxBytes + MultipartOverhead,
            }, ct);
        }
        catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException or IOException)
        {
            return (null, Problems.Field(field, tooLargeMessage, "file_too_large"));
        }

        var file = form.Files.GetFile(field);
        if (file is null) return (null, Problems.Field(field, "Upload a file.", "file_required"));
        if (file.Length > maxBytes) return (null, Problems.Field(field, tooLargeMessage, "file_too_large"));
        if (file.Length == 0) return (null, Problems.Field(field, "The file is empty.", "file_empty"));

        var bytes = new byte[file.Length];
        await using (var s = file.OpenReadStream())
            await s.ReadExactlyAsync(bytes, ct);
        return (new UploadedFile(bytes, file.FileName, file.ContentType), null);
    }
}
