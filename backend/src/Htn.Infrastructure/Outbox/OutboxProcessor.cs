using System.Text.Json;
using Htn.Domain.Outbox;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Email;
using Htn.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Htn.Infrastructure.Outbox;

public sealed class OutboxOptions
{
    public const string Section = "Outbox";

    /// <summary>Run the background worker. Tests turn it off and call <see cref="OutboxProcessor"/> directly.</summary>
    public bool Enabled { get; set; } = true;

    public int PollSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 20;
}

/// <summary>Delivers one batch of pending outbox messages. Rows are locked with SKIP LOCKED so several instances can run.</summary>
public sealed class OutboxProcessor(
    HtnDbContext db,
    IEmailSender sender,
    EmailRenderer renderer,
    RegistrationEmails registrationEmails,
    TimeProvider clock,
    IOptions<AppLinkOptions> links,
    IOptions<AccountTokenOptions> tokenOptions,
    IOptions<OutboxOptions> options,
    ILogger<OutboxProcessor> logger)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <returns>Number of messages attempted.</returns>
    public async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var batch = await db.Outbox
            .FromSql($"""
                SELECT * FROM outbox_messages
                WHERE status = 'Pending' AND next_attempt_at <= {now}
                ORDER BY created_at
                LIMIT {options.Value.BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        foreach (var message in batch)
        {
            try
            {
                var email = await RenderAsync(message);
                if (email is null)
                {
                    // Obsolete (e.g. paid before the billing went out, or the ticket was re-issued): nothing to deliver.
                    logger.LogInformation("Outbox message {Id} ({Type}) no longer applies and was skipped", message.Id, message.Type);
                    message.MarkSent(clock.GetUtcNow(), externalId: null);
                    continue;
                }
                var result = await sender.SendAsync(email, ct);
                message.MarkSent(clock.GetUtcNow(), result.ExternalId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Outbox message {Id} ({Type}) attempt {Attempt} failed", message.Id, message.Type, message.Attempts + 1);
                message.MarkAttemptFailed($"{ex.GetType().Name}: {ex.Message}", clock.GetUtcNow());
                if (message.Status == OutboxStatus.Failed)
                    logger.LogError("Outbox message {Id} ({Type}) failed after {Attempts} attempts and needs review", message.Id, message.Type, message.Attempts);
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return batch.Count;
    }

    private async Task<EmailMessage?> RenderAsync(OutboxMessage message)
    {
        switch (message.Type)
        {
            case EmailTypes.Invite:
            {
                var p = JsonSerializer.Deserialize<InviteEmailPayload>(message.PayloadJson, Json)
                        ?? throw new InvalidOperationException("Empty invite payload.");
                var token = message.Secret ?? throw new InvalidOperationException("Invite token missing.");
                return await renderer.InviteAsync(p, links.Value.Invite(token), (int)tokenOptions.Value.InviteLifetime.TotalHours);
            }
            case EmailTypes.PasswordReset:
            {
                var p = JsonSerializer.Deserialize<PasswordResetEmailPayload>(message.PayloadJson, Json)
                        ?? throw new InvalidOperationException("Empty reset payload.");
                var token = message.Secret ?? throw new InvalidOperationException("Reset token missing.");
                return await renderer.PasswordResetAsync(p, links.Value.Reset(token), (int)tokenOptions.Value.PasswordResetLifetime.TotalMinutes);
            }
            case RegistrationEmailTypes.Billing:
                return await registrationEmails.BillingAsync(Payload<BillingEmailPayload>(message), message.Secret);
            case RegistrationEmailTypes.Ticket:
                return await registrationEmails.TicketAsync(Payload<TicketEmailPayload>(message));
            case RegistrationEmailTypes.Cancelled:
                return await registrationEmails.CancelledAsync(Payload<RegistrationCancelledEmailPayload>(message));
            case RegistrationEmailTypes.Test:
                return await registrationEmails.TestAsync(Payload<EventTestEmailPayload>(message));
            default:
                throw new NotSupportedException($"Unknown outbox type '{message.Type}'.");
        }
    }

    private static T Payload<T>(OutboxMessage message) =>
        JsonSerializer.Deserialize<T>(message.PayloadJson, Json) ?? throw new InvalidOperationException($"Empty {message.Type} payload.");

    /// <summary>Billing email; <paramref name="rawProofToken"/> (Upload mode) travels in the secret column, never in the payload.</summary>
    public static OutboxMessage BillingEmail(Guid registrationId, string? rawProofToken, DateTimeOffset at) =>
        OutboxMessage.Create(RegistrationEmailTypes.Billing, JsonSerializer.Serialize(new BillingEmailPayload(registrationId), Json), rawProofToken, at);

    public static OutboxMessage TicketEmail(Guid attendeeId, string to, int ticketVersion, DateTimeOffset at) =>
        OutboxMessage.Create(RegistrationEmailTypes.Ticket, JsonSerializer.Serialize(new TicketEmailPayload(attendeeId, to, ticketVersion), Json), null, at);

    public static OutboxMessage CancelledEmail(Guid registrationId, DateTimeOffset at) =>
        OutboxMessage.Create(RegistrationEmailTypes.Cancelled, JsonSerializer.Serialize(new RegistrationCancelledEmailPayload(registrationId), Json), null, at);

    public static OutboxMessage EventTestEmail(EventTestEmailPayload payload, DateTimeOffset at) =>
        OutboxMessage.Create(RegistrationEmailTypes.Test, JsonSerializer.Serialize(payload, Json), null, at);

    public static OutboxMessage InviteEmail(InviteEmailPayload payload, string rawToken, DateTimeOffset at) =>
        OutboxMessage.Create(EmailTypes.Invite, JsonSerializer.Serialize(payload, Json), rawToken, at);

    public static OutboxMessage PasswordResetEmail(PasswordResetEmailPayload payload, string rawToken, DateTimeOffset at) =>
        OutboxMessage.Create(EmailTypes.PasswordReset, JsonSerializer.Serialize(payload, Json), rawToken, at);
}
