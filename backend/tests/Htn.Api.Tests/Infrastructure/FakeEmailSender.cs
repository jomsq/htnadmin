using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Htn.Infrastructure.Email;

namespace Htn.Api.Tests.Infrastructure;

/// <summary>Captures every email instead of calling Mailtrap.</summary>
public sealed partial class FakeEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public IReadOnlyList<EmailMessage> Sent => _sent.ToArray();

    public IReadOnlyList<EmailMessage> To(string email) =>
        _sent.Where(m => string.Equals(m.To, email, StringComparison.OrdinalIgnoreCase)).ToArray();

    public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct)
    {
        _sent.Enqueue(message);
        return Task.FromResult(new EmailSendResult("fake-" + Guid.NewGuid().ToString("N")));
    }

    [GeneratedRegex("/(invite|reset)/([A-Za-z0-9_%-]+)")]
    private static partial Regex LinkPattern();

    /// <summary>Extracts the raw token from the last invite/reset email to <paramref name="email"/>.</summary>
    public string LastToken(string email, string kind)
    {
        var message = To(email).Last(m => m.Text.Contains($"/{kind}/", StringComparison.Ordinal));
        var match = LinkPattern().Matches(message.Text).First(m => m.Groups[1].Value == kind);
        return Uri.UnescapeDataString(match.Groups[2].Value);
    }
}
