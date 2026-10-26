using Htn.Domain.Common;

namespace Htn.Domain.Files;

/// <summary>Metadata for an uploaded file. The bytes live in file storage under <see cref="Id"/>. Never deleted.</summary>
public sealed class StoredFile
{
    public const int ContentTypeMaxLength = 100;
    public const int PurposeMaxLength = 50;
    public const int FileNameMaxLength = 200;
    /// <summary>Default per-file limit (logos, CSVs). Some purposes allow more: see <see cref="StoredFilePurposes.MaxBytesFor"/>.</summary>
    public const long MaxBytes = 1024 * 1024;

    /// <summary>Hard ceiling for any stored file (also the database check constraint).</summary>
    public const long AbsoluteMaxBytes = 5 * 1024 * 1024;

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
        var max = StoredFilePurposes.MaxBytesFor(purpose);
        if (sizeBytes <= 0 || sizeBytes > max)
            throw new DomainException($"File must be between 1 byte and {max / (1024 * 1024)} MB.");
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
    public const string EventCover = "event_cover";

    /// <summary>Proof of payment from a registrant. Never in a public list: served only to Registrations.Manage.</summary>
    public const string PaymentProof = "payment_proof";

    public const long EventCoverMaxBytes = 2 * 1024 * 1024;
    public const long PaymentProofMaxBytes = 5 * 1024 * 1024;

    /// <summary>Largest width or height of a proof image (phone screenshots and camera photos).</summary>
    public const int PaymentProofMaxDimension = 8192;

    /// <summary>Purposes that any signed-in user may download through <c>GET /api/files/{id}</c>.</summary>
    public static readonly IReadOnlyList<string> PublicToMembers = [LogoLight, LogoDark];

    /// <summary>Purposes anyone (no sign-in) may download through <c>GET /api/public/files/{id}</c>: the public page and emails.</summary>
    public static readonly IReadOnlyList<string> PublicToEveryone = [LogoLight, LogoDark, EventCover];

    public static long MaxBytesFor(string purpose) => purpose switch
    {
        EventCover => EventCoverMaxBytes,
        PaymentProof => PaymentProofMaxBytes,
        _ => StoredFile.MaxBytes,
    };
}

public static class FileSignatures
{
    /// <summary>Largest width or height accepted for an uploaded image (keeps decode memory bounded for viewers).</summary>
    public const int MaxImageDimension = 4096;

    public const string PngContentType = "image/png";
    public const string JpegContentType = "image/jpeg";

    private static ReadOnlySpan<byte> Png => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>PNG signature followed by an IHDR chunk (the first chunk of every valid PNG).</summary>
    public static bool IsPng(ReadOnlySpan<byte> data) =>
        data.Length >= 33 && data[..8].SequenceEqual(Png) && data.Slice(12, 4).SequenceEqual("IHDR"u8);

    /// <summary>Width and height from the IHDR chunk (big-endian, bytes 16–19 and 20–23). Call only after <see cref="IsPng"/>.</summary>
    public static (uint Width, uint Height) PngSize(ReadOnlySpan<byte> data) =>
        (System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.Slice(16, 4)),
         System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.Slice(20, 4)));

    /// <summary>JPEG start-of-image marker followed by another marker (FF D8 FF).</summary>
    public static bool IsJpeg(ReadOnlySpan<byte> data) =>
        data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF;

    /// <summary>
    /// Width and height from the first SOF (start-of-frame) segment, walking the marker segments after SOI.
    /// Returns (0, 0) when the data is truncated or malformed, or no frame header comes before the image data.
    /// </summary>
    public static (uint Width, uint Height) JpegSize(ReadOnlySpan<byte> data)
    {
        if (!IsJpeg(data)) return (0, 0);
        var i = 2;
        while (i < data.Length)
        {
            if (data[i] != 0xFF) return (0, 0);
            while (i < data.Length && data[i] == 0xFF) i++; // fill bytes
            if (i >= data.Length) return (0, 0);
            var marker = data[i++];
            // Standalone markers without a length: TEM and RST0–7. SOI again is malformed; EOI/SOS before a frame header means no size.
            if (marker == 0x01 || marker is >= 0xD0 and <= 0xD7) continue;
            if (marker is 0xD8 or 0xD9 or 0xDA) return (0, 0);
            if (i + 2 > data.Length) return (0, 0);
            var length = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(data.Slice(i, 2));
            if (length < 2 || i + length > data.Length) return (0, 0);
            // SOF0–SOF15, except DHT (C4), JPG (C8) and DAC (CC).
            if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
            {
                if (length < 7) return (0, 0);
                var height = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(data.Slice(i + 3, 2));
                var width = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(data.Slice(i + 5, 2));
                return (width, height);
            }
            i += length;
        }
        return (0, 0);
    }

    /// <summary>
    /// Detects a PNG (or, when allowed, a JPEG) from its bytes, never the declared type, and reads its pixel size.
    /// Returns null when the bytes are not a supported image or the size can't be read.
    /// </summary>
    public static (string ContentType, uint Width, uint Height)? DetectImage(ReadOnlySpan<byte> data, bool allowJpeg)
    {
        if (IsPng(data))
        {
            var (w, h) = PngSize(data);
            return w == 0 || h == 0 ? null : (PngContentType, w, h);
        }
        if (allowJpeg && IsJpeg(data))
        {
            var (w, h) = JpegSize(data);
            return w == 0 || h == 0 ? null : (JpegContentType, w, h);
        }
        return null;
    }

    /// <summary>
    /// Removes metadata that can carry personal data (GPS position, device, names, comments, thumbnails) without
    /// re-encoding. JPEG keeps only APP0 (JFIF, without a thumbnail), APP2 (ICC colour profile) and APP14 (Adobe colour
    /// transform), and drops APP1 (EXIF/XMP), APP13 (Photoshop/IPTC), every other APPn and COM. PNG drops tEXt, iTXt,
    /// zTXt and eXIf chunks. Fails closed: returns null when the file can't be parsed all the way, so the caller rejects it
    /// instead of storing metadata it couldn't see.
    /// </summary>
    public static byte[]? StripMetadata(byte[] data)
    {
        if (IsPng(data)) return StripPng(data);
        if (IsJpeg(data)) return StripJpeg(data);
        return null;
    }

    private static bool KeepJpegSegment(byte marker, ReadOnlySpan<byte> payload) => marker switch
    {
        // JFIF header: kept unless it carries a thumbnail (Xthumb and Ythumb are bytes 12 and 13 of the payload).
        0xE0 => payload.Length >= 14 && payload[..5].SequenceEqual("JFIF\0"u8) && (payload[12] == 0 || payload[13] == 0),
        0xE2 or 0xEE => true,
        >= 0xE1 and <= 0xEF or 0xFE => false,
        _ => true,
    };

    private static byte[]? StripPng(byte[] data)
    {
        using var output = new MemoryStream(data.Length);
        output.Write(data, 0, 8);
        var i = 8;
        while (i + 12 <= data.Length)
        {
            var length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(i, 4));
            if (length > int.MaxValue || i + 12 + (long)length > data.Length) return null;
            var type = System.Text.Encoding.ASCII.GetString(data, i + 4, 4);
            var total = 12 + (int)length;
            if (type is not ("tEXt" or "iTXt" or "zTXt" or "eXIf")) output.Write(data, i, total);
            i += total;
            if (type == "IEND") return output.ToArray();
        }
        return null;
    }

    private static byte[]? StripJpeg(byte[] data)
    {
        using var output = new MemoryStream(data.Length);
        output.Write(data, 0, 2); // SOI
        var i = 2;
        while (i < data.Length)
        {
            if (data[i] != 0xFF) return null;
            var start = i;
            while (i < data.Length && data[i] == 0xFF) i++;
            if (i >= data.Length) return null;
            var marker = data[i++];
            if (marker == 0x01 || marker is >= 0xD0 and <= 0xD7)
            {
                output.Write(data, start, i - start);
                continue;
            }
            if (marker == 0xD9) // EOI
            {
                output.Write(data, start, i - start);
                return output.ToArray();
            }
            if (i + 2 > data.Length) return null;
            var length = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(i, 2));
            if (length < 2 || i + length > data.Length) return null;
            if (marker == 0xDA)
            {
                // Start of scan: the entropy-coded data and everything after it are copied as they are.
                output.Write(data, start, data.Length - start);
                return output.ToArray();
            }
            if (KeepJpegSegment(marker, data.AsSpan(i + 2, length - 2))) output.Write(data, start, i + length - start);
            i += length;
        }
        return null;
    }
}
