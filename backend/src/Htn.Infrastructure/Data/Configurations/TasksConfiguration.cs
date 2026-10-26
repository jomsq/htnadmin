using Htn.Domain.Committees;
using Htn.Domain.Events;
using Htn.Domain.Files;
using Htn.Domain.Organization;
using Htn.Domain.Tasks;
using Htn.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Htn.Infrastructure.Data.Configurations;

internal sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    /// <summary>One reference number per event: the backstop behind the counter on the event row.</summary>
    public const string NumberIndex = "ux_tasks_event_number";

    public void Configure(EntityTypeBuilder<TaskItem> b)
    {
        b.ToTable("tasks", t =>
        {
            t.HasCheckConstraint("ck_tasks_status", CheckConstraints.In("status", Enum.GetNames<TaskBoardStatus>()));
            t.HasCheckConstraint("ck_tasks_title",
                $"char_length(title) BETWEEN 1 AND {TaskItem.TitleMaxLength} AND title = btrim(title)");
            t.HasCheckConstraint("ck_tasks_description",
                $"description IS NULL OR char_length(description) BETWEEN 1 AND {TaskItem.DescriptionMaxLength}");
            t.HasCheckConstraint("ck_tasks_base_points", $"base_points BETWEEN 0 AND {TaskItem.MaxBasePoints}");
            t.HasCheckConstraint("ck_tasks_number", "number > 0");
            // The displayed reference always matches the number it came from.
            t.HasCheckConstraint("ck_tasks_reference", $"reference = '{TaskItem.ReferencePrefix}-' || number::text");
            t.HasCheckConstraint("ck_tasks_sort_order", $"sort_order BETWEEN 0 AND {TaskItem.MaxSortOrder}");
            // Labels are free text on a text[] column, so they are bounded in every direction: no NULLs, no empty
            // elements, at most MaxLabels of them and a bounded total length.
            t.HasCheckConstraint("ck_tasks_labels",
                $"array_position(labels, NULL) IS NULL AND array_position(labels, '') IS NULL "
                + $"AND cardinality(labels) <= {TaskItem.MaxLabels} "
                + $"AND char_length(array_to_string(labels, ',')) <= {TaskItem.LabelsTotalMaxLength}");
            // Done is only reached by approving, and reopening clears the stamp again.
            t.HasCheckConstraint("ck_tasks_approved", "(status = 'Done') = (approved_at IS NOT NULL)");
            t.HasCheckConstraint("ck_tasks_archived",
                $"is_archived = (archived_at IS NOT NULL) AND (is_archived OR archive_reason IS NULL) "
                + $"AND (archive_reason IS NULL OR char_length(archive_reason) BETWEEN 1 AND {TaskItem.ReasonMaxLength})");
        });
        b.HasKey(t => t.Id);
        b.Property(t => t.Id).ValueGeneratedNever();
        b.Property(t => t.Reference).HasMaxLength(TaskItem.ReferenceMaxLength).IsRequired();
        b.Property(t => t.Title).HasMaxLength(TaskItem.TitleMaxLength).IsRequired();
        b.Property(t => t.Description).HasMaxLength(TaskItem.DescriptionMaxLength);
        b.Property(t => t.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(t => t.ArchiveReason).HasMaxLength(TaskItem.ReasonMaxLength);
        b.Property(t => t.Labels).HasColumnType("text[]").HasDefaultValueSql("'{}'::text[]").IsRequired();
        // Two people moving or editing the same card at once: the second save gets 409 concurrent_change.
        b.Property<uint>(EventConfiguration.RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        b.Ignore(t => t.IsOpen);

        // Assignees and awards are keyed by (task_id, event_id), so a child can never cross events.
        b.HasAlternateKey(t => new { t.Id, t.EventId }).HasName("ak_tasks_id_event_id");

        b.HasIndex(t => new { t.EventId, t.Number }).IsUnique().HasDatabaseName(NumberIndex);
        // The board: one event's live cards, column by column, in display order.
        b.HasIndex(t => new { t.EventId, t.Status, t.SortOrder }).HasFilter("NOT is_archived");
        // The whole board in one scan (every column at once, in display order) — what GET /tasks reads.
        b.HasIndex(t => new { t.EventId, t.SortOrder, t.CreatedAt }).HasFilter("NOT is_archived");
        // The board filtered to one committee chip.
        b.HasIndex(t => new { t.EventId, t.CommitteeId, t.SortOrder }).HasFilter("NOT is_archived");
        // The archive view (?includeArchived=true), newest first.
        b.HasIndex(t => new { t.EventId, t.ArchivedAt }).HasFilter("is_archived");
        // Open-task counts per committee (committee cards and the deactivate guard).
        b.HasIndex(t => new { t.CommitteeId, t.Status }).HasFilter("NOT is_archived");

        b.HasOne<Event>().WithMany().HasForeignKey(t => t.EventId).OnDelete(DeleteBehavior.Restrict);
        // Keyed on (committee_id, event_id), so a task can never point at a committee of another event.
        b.HasOne<Committee>().WithMany().HasForeignKey(t => new { t.CommitteeId, t.EventId })
            .HasPrincipalKey(c => new { c.Id, c.EventId }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TaskAssigneeConfiguration : IEntityTypeConfiguration<TaskAssignee>
{
    public const string PersonIndex = "ux_task_assignees_person";
    public const string VolunteerIndex = "ux_task_assignees_volunteer";

    public void Configure(EntityTypeBuilder<TaskAssignee> b)
    {
        b.ToTable("task_assignees", t =>
        {
            t.HasCheckConstraint("ck_task_assignees_role", CheckConstraints.In("role", Enum.GetNames<TaskAssigneeRole>()));
            // A person or a volunteer, never both and never neither.
            t.HasCheckConstraint("ck_task_assignees_who", "(person_id IS NULL) <> (volunteer_id IS NULL)");
            // A volunteer only ever helps as an Assistant, and so never earns points.
            t.HasCheckConstraint("ck_task_assignees_volunteer_role", "volunteer_id IS NULL OR role = 'Assistant'");
        });
        b.HasKey(a => a.Id);
        b.Property(a => a.Id).ValueGeneratedNever();
        b.Property(a => a.Role).HasConversion<string>().HasMaxLength(10);
        b.Ignore(a => a.IsVolunteer);
        b.Ignore(a => a.EarnsPoints);

        b.HasIndex(a => a.TaskId);
        b.HasIndex(a => new { a.TaskId, a.PersonId }).IsUnique().HasDatabaseName(PersonIndex).HasFilter("person_id IS NOT NULL");
        b.HasIndex(a => new { a.TaskId, a.VolunteerId }).IsUnique().HasDatabaseName(VolunteerIndex).HasFilter("volunteer_id IS NOT NULL");
        // "My tasks" in the sidebar: the person's rows, with the task id in the index so the lookup is index-only.
        b.HasIndex(a => new { a.PersonId, a.TaskId }).HasFilter("person_id IS NOT NULL");

        // Keyed on (task_id, event_id) and (volunteer_id, event_id): the row carries its event, so a volunteer from
        // another event can never be assigned, whatever the API does.
        b.HasOne<TaskItem>().WithMany().HasForeignKey(a => new { a.TaskId, a.EventId })
            .HasPrincipalKey(t => new { t.Id, t.EventId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Person>().WithMany().HasForeignKey(a => a.PersonId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<EventVolunteer>().WithMany().HasForeignKey(a => new { a.VolunteerId, a.EventId })
            .HasPrincipalKey(v => new { v.Id, v.EventId }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TaskChecklistItemConfiguration : IEntityTypeConfiguration<TaskChecklistItem>
{
    public void Configure(EntityTypeBuilder<TaskChecklistItem> b)
    {
        b.ToTable("task_checklist_items", t =>
        {
            t.HasCheckConstraint("ck_task_checklist_items_text",
                $"char_length(text) BETWEEN 1 AND {TaskChecklistItem.TextMaxLength} AND text = btrim(text)");
            t.HasCheckConstraint("ck_task_checklist_items_done", "is_done = (done_at IS NOT NULL)");
            t.HasCheckConstraint("ck_task_checklist_items_sort_order", $"sort_order BETWEEN 0 AND {TaskItem.MaxSortOrder}");
        });
        b.HasKey(i => i.Id);
        b.Property(i => i.Id).ValueGeneratedNever();
        b.Property(i => i.Text).HasMaxLength(TaskChecklistItem.TextMaxLength).IsRequired();
        b.HasIndex(i => new { i.TaskId, i.SortOrder });
        b.HasOne<TaskItem>().WithMany().HasForeignKey(i => i.TaskId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TaskCommentConfiguration : IEntityTypeConfiguration<TaskComment>
{
    public void Configure(EntityTypeBuilder<TaskComment> b)
    {
        b.ToTable("task_comments", t =>
        {
            t.HasCheckConstraint("ck_task_comments_body", $"char_length(body) BETWEEN 1 AND {TaskComment.BodyMaxLength}");
            // Nothing is hard-deleted: deleting stamps the timestamp and always records why.
            t.HasCheckConstraint("ck_task_comments_deleted",
                $"(deleted_at IS NULL) = (delete_reason IS NULL) "
                + $"AND (delete_reason IS NULL OR char_length(delete_reason) BETWEEN 1 AND {TaskComment.ReasonMaxLength})");
            t.HasCheckConstraint("ck_task_comments_mentions",
                $"array_position(mentioned_person_ids, NULL) IS NULL AND cardinality(mentioned_person_ids) <= {TaskComment.MaxMentions}");
        });
        b.HasKey(c => c.Id);
        b.Property(c => c.Id).ValueGeneratedNever();
        b.Property(c => c.Body).HasMaxLength(TaskComment.BodyMaxLength).IsRequired();
        b.Property(c => c.DeleteReason).HasMaxLength(TaskComment.ReasonMaxLength);
        b.Property(c => c.MentionedPersonIds).HasColumnType("uuid[]").HasDefaultValueSql("'{}'::uuid[]").IsRequired();
        b.Ignore(c => c.IsDeleted);
        b.HasIndex(c => new { c.TaskId, c.CreatedAt });
        b.HasOne<TaskItem>().WithMany().HasForeignKey(c => c.TaskId).OnDelete(DeleteBehavior.Restrict);
        // author_user_id is a user id (never a person id), and the FK says so.
        b.HasOne<AppUser>().WithMany().HasForeignKey(c => c.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TaskAttachmentConfiguration : IEntityTypeConfiguration<TaskAttachment>
{
    public void Configure(EntityTypeBuilder<TaskAttachment> b)
    {
        b.ToTable("task_attachments", t =>
            t.HasCheckConstraint("ck_task_attachments_file_name", "char_length(file_name) BETWEEN 1 AND 200"));
        b.HasKey(a => a.Id);
        b.Property(a => a.Id).ValueGeneratedNever();
        b.Property(a => a.FileName).HasMaxLength(200).IsRequired();
        b.Ignore(a => a.IsRemoved);
        b.HasIndex(a => new { a.TaskId, a.UploadedAt });
        b.HasIndex(a => a.FileId).IsUnique();
        b.HasOne<TaskItem>().WithMany().HasForeignKey(a => a.TaskId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StoredFile>().WithMany().HasForeignKey(a => a.FileId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TaskActivityConfiguration : IEntityTypeConfiguration<TaskActivity>
{
    public void Configure(EntityTypeBuilder<TaskActivity> b)
    {
        b.ToTable("task_activities", t =>
        {
            t.HasCheckConstraint("ck_task_activities_kind", CheckConstraints.In("kind", TaskActivityKinds.All));
            t.HasCheckConstraint("ck_task_activities_text", $"char_length(text) BETWEEN 1 AND {TaskActivity.TextMaxLength}");
        });
        b.HasKey(a => a.Id);
        b.Property(a => a.Id).ValueGeneratedNever();
        b.Property(a => a.Kind).HasMaxLength(TaskActivity.KindMaxLength).IsRequired();
        b.Property(a => a.Text).HasMaxLength(TaskActivity.TextMaxLength).IsRequired();
        b.HasIndex(a => new { a.TaskId, a.At });
        b.HasOne<TaskItem>().WithMany().HasForeignKey(a => a.TaskId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PointsAwardConfiguration : IEntityTypeConfiguration<PointsAward>
{
    /// <summary>At most one live award per person per task: the backstop behind the idempotent approve.</summary>
    public const string LiveAwardIndex = "ux_points_awards_live_task_person";

    public void Configure(EntityTypeBuilder<PointsAward> b)
    {
        b.ToTable("points_awards", t =>
        {
            t.HasCheckConstraint("ck_points_awards_role", CheckConstraints.In("role", Enum.GetNames<TaskAssigneeRole>()));
            t.HasCheckConstraint("ck_points_awards_base_points", $"base_points BETWEEN 0 AND {TaskItem.MaxBasePoints}");
            t.HasCheckConstraint("ck_points_awards_multiplier", "multiplier BETWEEN 0 AND 2");
            // The snapshot has to add up: points are base × multiplier at the moment of the award.
            t.HasCheckConstraint("ck_points_awards_points", "points = round(base_points * multiplier, 2)");
            // A reversal always has a reason, and can never predate the award it reverses.
            t.HasCheckConstraint("ck_points_awards_reversal",
                $"(reversed_at IS NULL) = (reversal_reason IS NULL) "
                + $"AND (reversed_at IS NULL OR reversed_at >= awarded_at) "
                + $"AND (reversal_reason IS NULL OR char_length(reversal_reason) BETWEEN 1 AND {PointsAward.ReasonMaxLength})");
        });
        b.HasKey(a => a.Id);
        b.Property(a => a.Id).ValueGeneratedNever();
        b.Property(a => a.Role).HasConversion<string>().HasMaxLength(10);
        b.Property(a => a.Multiplier).HasPrecision(4, 2);
        b.Property(a => a.Points).HasPrecision(8, 2);
        b.Property(a => a.ReversalReason).HasMaxLength(PointsAward.ReasonMaxLength);
        b.Ignore(a => a.IsLive);

        b.HasIndex(a => new { a.TaskId, a.PersonId }).IsUnique().HasDatabaseName(LiveAwardIndex).HasFilter("reversed_at IS NULL");
        b.HasIndex(a => a.TaskId);
        // The leaderboard (frame 14) groups live awards by person and committee over one of three windows: one event,
        // the current Manila quarter, or all time. These partial indexes cover each of those scans, INCLUDE-ing the
        // aggregated columns so the grouping is an index-only scan and the award rows are never touched.
        b.HasIndex(a => new { a.PersonId, a.AwardedAt }).HasFilter("reversed_at IS NULL")
            .IncludeProperties(a => new { a.Points, a.EventId, a.CommitteeId, a.Role });
        b.HasIndex(a => new { a.EventId, a.AwardedAt, a.PersonId }).HasFilter("reversed_at IS NULL")
            .IncludeProperties(a => new { a.Points, a.Role, a.CommitteeId });
        b.HasIndex(a => new { a.CommitteeId, a.AwardedAt, a.PersonId }).HasFilter("reversed_at IS NULL")
            .IncludeProperties(a => new { a.Points, a.Role });
        // The default scope is "this quarter" across every event, which filters on awarded_at alone.
        b.HasIndex(a => new { a.AwardedAt, a.PersonId }).HasFilter("reversed_at IS NULL")
            .IncludeProperties(a => new { a.Points, a.Role, a.EventId, a.CommitteeId });

        // The snapshot is keyed to the task *and* the committee of one event: (task_id, event_id) and
        // (committee_id, event_id), so an award can never name a task or a committee of another event.
        b.HasOne<TaskItem>().WithMany().HasForeignKey(a => new { a.TaskId, a.EventId })
            .HasPrincipalKey(t => new { t.Id, t.EventId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Event>().WithMany().HasForeignKey(a => a.EventId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Committee>().WithMany().HasForeignKey(a => new { a.CommitteeId, a.EventId })
            .HasPrincipalKey(c => new { c.Id, c.EventId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Person>().WithMany().HasForeignKey(a => a.PersonId).OnDelete(DeleteBehavior.Restrict);
    }
}
