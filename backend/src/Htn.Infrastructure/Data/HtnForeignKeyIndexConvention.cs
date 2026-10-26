using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace Htn.Infrastructure.Data;

/// <summary>
/// EF Core's <see cref="ForeignKeyIndexConvention"/> with the indexes for the composite <c>(child_id, event_id)</c>
/// foreign keys left out (from the Phase 4 database review).
/// <para>
/// Those keys exist to make a cross-event row impossible, not to be searched: every query filters by the event or by
/// the parent id, which is already the leading column of an index we declare by hand, and none of these rows is ever
/// deleted (the guard triggers refuse it), so there is no cascade scan to support either. Each of these indexes would
/// be pure write cost. Replacing the convention — rather than removing the index afterwards — is what makes it stick:
/// removing an index makes the stock convention put the foreign-key index straight back.
/// </para>
/// </summary>
internal sealed class HtnForeignKeyIndexConvention(ProviderConventionSetBuilderDependencies dependencies)
    : ForeignKeyIndexConvention(dependencies)
{
    /// <summary>Entity → the foreign-key property lists that get no index of their own.</summary>
    private static readonly (Type Entity, string[] Properties)[] Redundant =
    [
        // Phase 4A: the (committee_id, event_id) foreign keys.
        (typeof(Domain.Tasks.TaskItem), ["CommitteeId", "EventId"]),
        (typeof(Domain.Committees.CommitteeMember), ["CommitteeId", "EventId"]),
        (typeof(Domain.Committees.EventVolunteer), ["CommitteeId", "EventId"]),
        // Phase 4 review: the new cross-event keys on the task children.
        (typeof(Domain.Tasks.TaskAssignee), ["TaskId", "EventId"]),
        (typeof(Domain.Tasks.PointsAward), ["TaskId", "EventId"]),
        (typeof(Domain.Tasks.PointsAward), ["CommitteeId", "EventId"]),
    ];

    protected override IConventionIndex? CreateIndex(
        IReadOnlyList<IConventionProperty> properties, bool unique, IConventionEntityTypeBuilder entityTypeBuilder)
    {
        var clrType = entityTypeBuilder.Metadata.ClrType;
        var names = properties.Select(p => p.Name).ToArray();
        if (Redundant.Any(r => r.Entity == clrType && r.Properties.SequenceEqual(names))) return null;
        return base.CreateIndex(properties, unique, entityTypeBuilder);
    }
}
