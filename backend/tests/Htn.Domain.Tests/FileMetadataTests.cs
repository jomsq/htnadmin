using System.Text;
using Htn.Domain.Files;
using Htn.Domain.Registrations;

namespace Htn.Domain.Tests;

public sealed class FileMetadataTests
{
    private static byte[] Segment(byte marker, byte[] payload)
    {
        var length = payload.Length + 2;
        return [0xFF, marker, (byte)(length >> 8), (byte)length, .. payload];
    }

    private static byte[] Jpeg(params byte[][] segments) =>
        [0xFF, 0xD8, .. segments.SelectMany(s => s), 0xFF, 0xDA, 0x00, 0x02, 0x11, 0x22, 0x33, 0xFF, 0xD9];

    private static readonly byte[] Sof0 = Segment(0xC0, [8, 0, 10, 0, 20, 1, 1, 0x11, 0]); // 20×10

    [Fact]
    public void Jpeg_app1_to_app15_and_comments_are_removed_but_jfif_and_image_data_stay()
    {
        var jfif = Segment(0xE0, [.. "JFIF\0"u8, 1, 1, 0, 0, 1, 0, 1, 0, 0]);
        var exif = Segment(0xE1, [.. "Exif\0\0"u8, .. "GPS 18.19N 120.59E"u8]);
        var xmp = Segment(0xED, [.. "Photoshop 3.0 owner=Juan"u8]);
        var comment = Segment(0xFE, [.. "taken on Juan's phone"u8]);
        var input = Jpeg(jfif, exif, xmp, comment, Sof0);

        var output = FileSignatures.StripMetadata(input);
        var text = Encoding.Latin1.GetString(output);
        Assert.DoesNotContain("GPS", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Juan", text, StringComparison.Ordinal);
        Assert.Contains("JFIF", text, StringComparison.Ordinal);
        Assert.Equal(Jpeg(jfif, Sof0), output);
        Assert.Equal((20u, 10u), FileSignatures.JpegSize(output));
    }

    [Fact]
    public void Png_text_and_exif_chunks_are_removed()
    {
        static byte[] Chunk(string type, byte[] data)
        {
            var c = new byte[12 + data.Length];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(c, (uint)data.Length);
            Encoding.ASCII.GetBytes(type).CopyTo(c, 4);
            data.CopyTo(c, 8);
            return c;
        }
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        var ihdr = Chunk("IHDR", [0, 0, 0, 1, 0, 0, 0, 1, 8, 6, 0, 0, 0]);
        var idat = Chunk("IDAT", [1, 2, 3]);
        var iend = Chunk("IEND", []);
        byte[] input =
        [
            .. signature, .. ihdr, .. Chunk("tEXt", [.. "Author\0Juan"u8]), .. Chunk("iTXt", [.. "GPS"u8]),
            .. Chunk("zTXt", [.. "x\0\0"u8]), .. Chunk("eXIf", [.. "MM\0*"u8]), .. idat, .. iend, .. "trailing"u8,
        ];
        var output = FileSignatures.StripMetadata(input);
        Assert.Equal([.. signature, .. ihdr, .. idat, .. iend], output);
        Assert.Equal((1u, 1u), FileSignatures.PngSize(output));
    }

    [Fact]
    public void Files_that_cannot_be_walked_fail_closed()
    {
        byte[] truncatedJpeg = [0xFF, 0xD8, 0xFF, 0xE1, 0x00, 0x40, 1, 2];
        Assert.Null(FileSignatures.StripMetadata(truncatedJpeg));
        byte[] jpegWithoutScan = [0xFF, 0xD8, .. Sof0];
        Assert.Null(FileSignatures.StripMetadata(jpegWithoutScan));
        Assert.Null(FileSignatures.StripMetadata([.. "GIF89a"u8]));
        byte[] pngWithoutEnd = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, .. "IHDR"u8, 0, 0, 0, 1, 0, 0, 0, 1, 8, 6, 0, 0, 0, 1, 2, 3, 4];
        Assert.Null(FileSignatures.StripMetadata(pngWithoutEnd));
    }

    [Fact]
    public void Jpeg_keeps_icc_and_adobe_segments_and_drops_photoshop_and_jfif_thumbnails()
    {
        var icc = Segment(0xE2, [.. "ICC_PROFILE\0"u8, 1, 1, 9, 9]);
        var adobe = Segment(0xEE, [.. "Adobe"u8, 0, 100, 0, 0, 0, 0, 1]);
        var photoshop = Segment(0xED, [.. "Photoshop 3.0\0IPTC by Juan"u8]);
        var app5 = Segment(0xE5, [.. "vendor Juan"u8]);
        var jfifWithThumb = Segment(0xE0, [.. "JFIF\0"u8, 1, 1, 0, 0, 1, 0, 1, 2, 2, .. new byte[12]]);
        var output = FileSignatures.StripMetadata(Jpeg(jfifWithThumb, icc, photoshop, app5, adobe, Sof0));
        Assert.Equal(Jpeg(icc, adobe, Sof0), output);
    }

    [Theory]
    [InlineData("Ma. Niña", true)]
    [InlineData("Santos, Jr.", true)]
    [InlineData("Dela Cruz-Ñañez", true)]
    [InlineData("O’Brien", true)]
    [InlineData("Juan https://x", false)]
    [InlineData("Juan@x", false)]
    [InlineData("<b>", false)]
    [InlineData("Juan2", false)]
    [InlineData("-Juan", false)]
    [InlineData("Ju\u0000an", false)]
    [InlineData("Santos, Jr.", true)]
    [InlineData("St. Luke's", true)]
    [InlineData("Ma.Niña", false)]
    [InlineData("gcash-ph.com", false)]
    [InlineData("Juan.", true)]
    public void Name_rules(string value, bool ok) => Assert.Equal(ok, RegistrationForm.IsValidName(value));

    [Theory]
    [InlineData("DICT Region 1", true)]
    [InlineData("R&D (IT/Security), Inc.", true)]
    [InlineData("3M Philippines", true)]
    [InlineData("see https://spam.example", false)]
    [InlineData("a:b", false)]
    [InlineData("St. Luke's Medical Center", true)]
    [InlineData("Ateneo de Manila Univ., Inc.", true)]
    [InlineData("pay at gcash-ph.com", false)]
    [InlineData("Version 2.0", false)]
    [InlineData("Inc./Ltd", false)]
    [InlineData("Co. /Ltd", true)]
    [InlineData("<script>", false)]
    public void Company_and_occupation_rules(string value, bool ok) => Assert.Equal(ok, RegistrationForm.IsValidOrgText(value));
}
