using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Htn.Infrastructure.Email;

/// <summary>Sends through the Mailtrap Email API. Retries (429/5xx) come from the resilience handler on the typed client.</summary>
public sealed class MailtrapEmailSender(HttpClient http, IOptions<MailtrapOptions> options, ILogger<MailtrapEmailSender> logger) : IEmailSender
{
    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct)
    {
        var o = options.Value;
        var from = o.FromAddress();
        var body = new MailtrapRequest(
            new MailtrapAddress(from.Address, string.IsNullOrEmpty(from.DisplayName) ? null : from.DisplayName),
            [new MailtrapAddress(message.To, message.ToName)],
            message.Subject, message.Html, message.Text, message.Category);

        using var request = new HttpRequestMessage(HttpMethod.Post, o.SendUri()) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.ApiToken);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            if (text.Length > 500) text = text[..500];
            throw new HttpRequestException($"Mailtrap returned {(int)response.StatusCode}: {text}", null, response.StatusCode);
        }

        var result = await response.Content.ReadFromJsonAsync<MailtrapResponse>(ct);
        var id = result?.MessageIds is { Length: > 0 } ids ? ids[0] : null;
        logger.LogInformation("Mailtrap accepted {Category} email, message id {MessageId}", message.Category, id);
        return new EmailSendResult(id);
    }

    private sealed record MailtrapAddress(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name);

    private sealed record MailtrapRequest(
        [property: JsonPropertyName("from")] MailtrapAddress From,
        [property: JsonPropertyName("to")] MailtrapAddress[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("html")] string Html,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("category")] string Category);

    private sealed record MailtrapResponse(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("message_ids")] string[]? MessageIds);
}
