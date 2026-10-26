using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Htn.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Htn.Infrastructure.Identity;

/// <summary>
/// RFC 6238 authenticator codes (SHA-1, 30 s, 6 digits, one step of clock drift either way) that can be used only once:
/// the accepted time step is stored on the user with an atomic conditional update, and any code whose step is not
/// newer than the last accepted one is rejected. Used for both sign-in and enabling 2FA.
/// </summary>
public sealed class ReplayProtectedAuthenticatorTokenProvider(HtnDbContext db, TimeProvider clock) : IUserTwoFactorTokenProvider<AppUser>
{
    public const string ProviderName = "HtnAuthenticator";
    private const int StepSeconds = 30;
    private const int AllowedDriftSteps = 1;

    public Task<string> GenerateAsync(string purpose, UserManager<AppUser> manager, AppUser user) =>
        Task.FromResult(string.Empty); // codes come from the user's authenticator app

    public async Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<AppUser> manager, AppUser user) =>
        !string.IsNullOrWhiteSpace(await manager.GetAuthenticatorKeyAsync(user));

    public async Task<bool> ValidateAsync(string purpose, string token, UserManager<AppUser> manager, AppUser user)
    {
        if (token is not { Length: 6 } || !token.All(char.IsAsciiDigit)) return false;
        var key = await manager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(key)) return false;
        var keyBytes = Base32Decode(key);
        if (keyBytes.Length == 0) return false;

        var current = CurrentStep(clock.GetUtcNow());
        long? matched = null;
        for (var step = current - AllowedDriftSteps; step <= current + AllowedDriftSteps; step++)
        {
            var expected = Encoding.ASCII.GetBytes(Code(keyBytes, step));
            if (CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(token)))
                matched = step;
        }
        if (matched is not { } accepted) return false;

        // Race-safe: of two requests with the same code, only one moves the stored step forward.
        var rows = await db.Users
            .Where(u => u.Id == user.Id && (u.LastTotpTimeStep == null || u.LastTotpTimeStep < accepted))
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastTotpTimeStep, accepted));
        if (rows != 1) return false;
        user.LastTotpTimeStep = accepted;
        return true;
    }

    public static long CurrentStep(DateTimeOffset now) => now.ToUnixTimeSeconds() / StepSeconds;

    public static string Code(byte[] key, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        Span<byte> hash = stackalloc byte[20];
        HMACSHA1.HashData(key, counter, hash);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    public static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new List<byte>(input.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in input.TrimEnd('='))
        {
            if (c == ' ') continue;
            var value = alphabet.IndexOf(char.ToUpperInvariant(c), StringComparison.Ordinal);
            if (value < 0) return [];
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }
        return [.. output];
    }
}
