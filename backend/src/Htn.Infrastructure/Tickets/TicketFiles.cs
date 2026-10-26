using System.Globalization;
using System.Text;
using QRCoder;

namespace Htn.Infrastructure.Tickets;

/// <summary>QR code images for ticket emails.</summary>
public static class TicketQr
{
    /// <summary>A black-on-white PNG (error correction M, 8 px per module, quiet zone included).</summary>
    public static byte[] Png(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(8);
    }
}

/// <summary>Builds a single-event iCalendar file (RFC 5545) for the "add to calendar" attachment.</summary>
public static class IcsCalendar
{
    public static string Event(
        string uid, DateTimeOffset start, DateTimeOffset end, string summary, string? location, string? description, DateTimeOffset stamp)
    {
        var sb = new StringBuilder();
        void Line(string content) => sb.Append(Fold(content)).Append("\r\n");
        Line("BEGIN:VCALENDAR");
        Line("VERSION:2.0");
        Line("PRODID:-//HackTheNorth.PH//HTN Admin//EN");
        Line("CALSCALE:GREGORIAN");
        Line("METHOD:PUBLISH");
        Line("BEGIN:VEVENT");
        Line("UID:" + Escape(uid));
        Line("DTSTAMP:" + Utc(stamp));
        Line("DTSTART:" + Utc(start));
        Line("DTEND:" + Utc(end));
        Line("SUMMARY:" + Escape(summary));
        if (!string.IsNullOrWhiteSpace(location)) Line("LOCATION:" + Escape(location));
        if (!string.IsNullOrWhiteSpace(description)) Line("DESCRIPTION:" + Escape(description));
        Line("END:VEVENT");
        Line("END:VCALENDAR");
        return sb.ToString();
    }

    private static string Utc(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    /// <summary>TEXT escaping (RFC 5545 §3.3.11): backslash, semicolon, comma and newlines; other control characters are dropped.</summary>
    public static string Escape(string value)
    {
        var sb = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '\\': sb.Append(@"\\"); break;
                case ';': sb.Append(@"\;"); break;
                case ',': sb.Append(@"\,"); break;
                case '\r':
                    if (i + 1 < value.Length && value[i + 1] == '\n') i++;
                    sb.Append(@"\n");
                    break;
                case '\n': sb.Append(@"\n"); break;
                default:
                    if (!char.IsControl(c)) sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Folds a content line at 75 octets (UTF-8), never splitting a multi-byte character (RFC 5545 §3.1).</summary>
    public static string Fold(string line)
    {
        if (Encoding.UTF8.GetByteCount(line) <= 75) return line;
        var sb = new StringBuilder();
        var octets = 0;
        var limit = 75;
        var e = StringInfo.GetTextElementEnumerator(line);
        while (e.MoveNext())
        {
            var element = (string)e.Current;
            var size = Encoding.UTF8.GetByteCount(element);
            if (octets + size > limit)
            {
                sb.Append("\r\n ");
                octets = 0;
                limit = 74; // the leading space counts
            }
            sb.Append(element);
            octets += size;
        }
        return sb.ToString();
    }
}
