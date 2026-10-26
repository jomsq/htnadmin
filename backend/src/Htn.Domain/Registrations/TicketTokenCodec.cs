using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Htn.Domain.Registrations;

/// <summary>What a valid ticket QR says: which attendee of which event, at which ticket version.</summary>
public readonly record struct TicketTokenPayload(Guid EventId, Guid AttendeeId, int TicketVersion);

/// <summary>
/// The QR payload: <c>base64url(eventId|attendeeId|ticketVersion)</c> + <c>.</c> + <c>base64url(HMAC-SHA256)</c>.
/// The MAC covers a fixed domain prefix and the encoded payload exactly as sent, so there is a single valid spelling.
/// Verification is constant-time. The short participant ID alone is never a ticket (desk lookup only).
/// </summary>
public static class TicketTokenCodec
{
    public const int MinKeyBytes = 32;

    /// <summary>Longest token accepted before any work is done (a real one is about 140 characters).</summary>
    public const int MaxTokenLength = 256;

    private const string Prefix = "htn.ticket.v1.";

    public static string Sign(ReadOnlySpan<byte> key, TicketTokenPayload payload)
    {
        EnsureKey(key);
        if (payload.TicketVersion < 1) throw new ArgumentOutOfRangeException(nameof(payload), "Ticket version starts at 1.");
        var text = string.Create(CultureInfo.InvariantCulture,
            $"{payload.EventId:D}|{payload.AttendeeId:D}|{payload.TicketVersion}");
        var body = Base64Url(Encoding.ASCII.GetBytes(text));
        return body + "." + Base64Url(Mac(key, body));
    }

    /// <summary>Parses and checks the signature. Any malformed or tampered token returns false.</summary>
    public static bool TryVerify(ReadOnlySpan<byte> key, string? token, out TicketTokenPayload payload)
    {
        payload = default;
        EnsureKey(key);
        if (string.IsNullOrEmpty(token) || token.Length > MaxTokenLength) return false;
        var dot = token.IndexOf('.');
        if (dot <= 0 || dot != token.LastIndexOf('.') || dot == token.Length - 1) return false;
        var body = token[..dot];
        var sigText = token[(dot + 1)..];
        if (!IsBase64Url(body) || !IsBase64Url(sigText)) return false;

        var expected = Mac(key, body);
        byte[] given;
        try
        {
            given = FromBase64Url(sigText);
        }
        catch (FormatException)
        {
            return false;
        }
        // Compare the re-encoded signature too, so non-canonical base64 spellings of the same bytes are rejected.
        if (!CryptographicOperations.FixedTimeEquals(expected, given) || !string.Equals(Base64Url(given), sigText, StringComparison.Ordinal))
            return false;

        string text;
        try
        {
            text = Encoding.ASCII.GetString(FromBase64Url(body));
        }
        catch (FormatException)
        {
            return false;
        }
        var parts = text.Split('|');
        if (parts.Length != 3
            || !Guid.TryParseExact(parts[0], "D", out var eventId)
            || !Guid.TryParseExact(parts[1], "D", out var attendeeId)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            || version < 1)
            return false;
        payload = new TicketTokenPayload(eventId, attendeeId, version);
        return true;
    }

    private static byte[] Mac(ReadOnlySpan<byte> key, string body) =>
        HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(Prefix + body));

    private static void EnsureKey(ReadOnlySpan<byte> key)
    {
        if (key.Length < MinKeyBytes) throw new ArgumentException($"The ticket signing key must be at least {MinKeyBytes} bytes.", nameof(key));
    }

    private static bool IsBase64Url(string s) => s.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public static string Base64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string s)
    {
        var b = s.Replace('-', '+').Replace('_', '/');
        b = (b.Length % 4) switch
        {
            0 => b,
            2 => b + "==",
            3 => b + "=",
            _ => throw new FormatException("Invalid base64url length."),
        };
        return Convert.FromBase64String(b);
    }
}
