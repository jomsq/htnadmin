using System.Text.Json;
using System.Text.Json.Serialization;
using Htn.Domain.Audit;
using Htn.Infrastructure.Data;

namespace Htn.Infrastructure.Audit;

public interface IAuditLog
{
    /// <summary>Adds an append-only audit entry to the current unit of work. The caller saves.</summary>
    void Record(Guid? actorId, string action, string entityType, string entityId, object? before = null, object? after = null, string? reason = null);
}

public sealed class AuditLog(HtnDbContext db, TimeProvider clock) : IAuditLog
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public void Record(Guid? actorId, string action, string entityType, string entityId, object? before = null, object? after = null, string? reason = null)
    {
        db.AuditEntries.Add(AuditEntry.Create(
            clock.GetUtcNow(), actorId, action, entityType, entityId,
            before is null ? null : JsonSerializer.Serialize(before, Json),
            after is null ? null : JsonSerializer.Serialize(after, Json),
            reason));
    }
}
