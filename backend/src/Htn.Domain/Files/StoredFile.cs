using Htn.Domain.Common;

namespace Htn.Domain.Files;

/// <summary>Metadata for an uploaded file. The bytes live in file storage under <see cref="Id"/>. Never deleted.</summary>
public sealed class StoredFile
{
    public const int ContentTypeMaxLength = 100;
    public const int PurposeMaxLength = 50;
    public const int FileNameMaxLength = 200;
    public const long MaxBytes = 1024 * 1024;

    private StoredFile() { }

    public Guid Id { get; private set; }
    public string ContentType { get; private set; } = default!;
    public long SizeBytes { get; private set; }

    /// <summary>Lower-case hex SHA-256 of the content.</summary>
    public string Sha256 { get; private set; } = default!;

    public string Purpose { get; private set; } = default!;
    public string? OriginalName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedById { get; private set; }

    public static StoredFile Create(
        string contentType, long sizeBytes, string sha256, string purpose, string? originalName, DateTimeOffset at, Guid? actorId)
    {
        if (sizeBytes is <= 0 or > MaxBytes) throw new DomainException("File must be between 1 byte and 1 MB.");
        if (sha256 is not { Length: 64 }) throw new DomainException("Invalid checksum.");
        var name = originalName?.Trim();
        if (name is { Length: > FileNameMaxLength }) name = name[..FileNameMaxLength];
        return new StoredFile
        {
            Id = Guid.CreateVersion7(at),
            ContentType = contentType,
            SizeBytes = sizeBytes,
            Sha256 = sha256,
            Purpose = purpose,
            OriginalName = string.IsNullOrEmpty(name) ? null : name,
            CreatedAt = at,
            CreatedById = actorId,
        };
    }
}

public static class StoredFilePurposes
{
    public const string LogoLight = "logo_light";
    public const string LogoDark = "logo_dark";

    /// <summary>Purposes that any signed-in user may download through <c>GET /api/files/{id}</c>.</summary>
    public static readonly IReadOnlyList<string> PublicToMembers = [LogoLight, LogoDark];
}

public static class FileSignatures
{
    /// <summary>Largest width or height accepted for an uploaded image (keeps decode memory bounded for viewers).</summary>
    public const int MaxImageDimension = 4096;

    private static ReadOnlySpan<byte> Png => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>PNG signature followed by an IHDR chunk (the first chunk of every valid PNG).</summary>
    public static bool IsPng(ReadOnlySpan<byte> data) =>
        data.Length >= 33 && data[..8].SequenceEqual(Png) && data.Slice(12, 4).SequenceEqual("IHDR"u8);

    /// <summary>Width and height from the IHDR chunk (big-endian, bytes 16–19 and 20–23). Call only after <see cref="IsPng"/>.</summary>
    public static (uint Width, uint Height) PngSize(ReadOnlySpan<byte> data) =>
        (System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.Slice(16, 4)),
         System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.Slice(20, 4)));
}
