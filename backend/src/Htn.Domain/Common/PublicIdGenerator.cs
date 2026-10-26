using System.Security.Cryptography;

namespace Htn.Domain.Common;

public sealed class PublicIdGenerator : IPublicIdGenerator
{
    /// <summary>
    /// Crockford base32 (which already drops I, L, O, U) minus 0 and 1, so there are no look-alikes
    /// for O/I/L when IDs are read aloud or typed from a receipt. 30 symbols.
    /// </summary>
    public const string Alphabet = "23456789ABCDEFGHJKMNPQRSTVWXYZ";

    public const string ParticipantPrefix = "HTN";

    public string NewParticipantId()
    {
        // GetString picks uniformly (rejection sampling), so there is no modulo bias.
        var body = RandomNumberGenerator.GetString(Alphabet, 8);
        return $"{ParticipantPrefix}-{body[..4]}-{body[4..]}";
    }

    public string NewCode(int length)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);
        return RandomNumberGenerator.GetString(Alphabet, length);
    }

    public string NewToken(int bytes = 32)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bytes, 16);
        Span<byte> buffer = stackalloc byte[bytes];
        RandomNumberGenerator.Fill(buffer);
        return Convert.ToBase64String(buffer).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
