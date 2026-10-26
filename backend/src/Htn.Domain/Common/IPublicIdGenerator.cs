namespace Htn.Domain.Common;

/// <summary>
/// Generates identifiers that are shown to the public (participant IDs, voucher suffixes, link tokens).
/// Never sequential; always from a CSPRNG.
/// </summary>
public interface IPublicIdGenerator
{
    /// <summary>Participant-style ID: <c>HTN-XXXX-XXXX</c> using <see cref="PublicIdGenerator.Alphabet"/>.</summary>
    string NewParticipantId();

    /// <summary>Random code of <paramref name="length"/> characters from <see cref="PublicIdGenerator.Alphabet"/>.</summary>
    string NewCode(int length);

    /// <summary>URL-safe (base64url, no padding) random token with <paramref name="bytes"/> bytes of entropy.</summary>
    string NewToken(int bytes = 32);
}
