using System.Text;
using Htn.Domain.Registrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Htn.Infrastructure.Tickets;

/// <summary>Signs and verifies ticket QR payloads (see <see cref="TicketTokenCodec"/> for the format).</summary>
public interface ITicketToken
{
    string Issue(Guid eventId, Guid attendeeId, int ticketVersion);

    /// <summary>Constant-time signature check. False for anything malformed or tampered.</summary>
    bool TryVerify(string? token, out TicketTokenPayload payload);
}

/// <summary>
/// Key from <c>Tickets:SigningKey</c> or the <c>TICKET_SIGNING_KEY</c> environment variable (at least 32 bytes, UTF-8).
/// Outside Development a missing key fails startup (<see cref="TicketSigning.EnsureConfigured"/>); Development falls
/// back to a published dev-only key.
/// </summary>
public sealed class TicketToken : ITicketToken
{
    private readonly byte[] _key;

    public TicketToken(IConfiguration config, IHostEnvironment env) => _key = TicketSigning.Key(config, env);

    public string Issue(Guid eventId, Guid attendeeId, int ticketVersion) =>
        TicketTokenCodec.Sign(_key, new TicketTokenPayload(eventId, attendeeId, ticketVersion));

    public bool TryVerify(string? token, out TicketTokenPayload payload) => TicketTokenCodec.TryVerify(_key, token, out payload);
}

public static class TicketSigning
{
    public const string ConfigKey = "Tickets:SigningKey";
    public const string EnvironmentVariable = "TICKET_SIGNING_KEY";

    /// <summary>Only ever used in Development when no key is configured. Anyone can forge tickets with it.</summary>
    public const string DevelopmentKey = "htn-dev-only-ticket-signing-key-do-not-use-in-production";

    public static string? Configured(IConfiguration config) =>
        config[ConfigKey] is { Length: > 0 } k ? k : config[EnvironmentVariable] is { Length: > 0 } e ? e : null;

    /// <summary>Throws when the key is missing outside Development, or shorter than 32 bytes anywhere.</summary>
    public static void EnsureConfigured(IConfiguration config, IHostEnvironment env) => _ = Key(config, env);

    public static byte[] Key(IConfiguration config, IHostEnvironment env)
    {
        var configured = Configured(config);
        if (configured is null)
        {
            if (env.IsDevelopment()) return Encoding.UTF8.GetBytes(DevelopmentKey);
            throw new InvalidOperationException(
                $"{EnvironmentVariable} is not set in the {env.EnvironmentName} environment, so ticket QR codes can't be signed. " +
                $"Set {EnvironmentVariable} (or Tickets__SigningKey) to at least 32 random bytes, e.g. `openssl rand -base64 48`.");
        }
        var key = Encoding.UTF8.GetBytes(configured);
        if (key.Length < TicketTokenCodec.MinKeyBytes)
            throw new InvalidOperationException($"{EnvironmentVariable} must be at least {TicketTokenCodec.MinKeyBytes} bytes long.");
        return key;
    }
}
