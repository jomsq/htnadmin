using Htn.Domain.Common;
using Htn.Domain.Files;
using Htn.Domain.Tasks;

namespace Htn.Domain.Tests;

public class TaskItemTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 8, 0, 0, TimeSpan.Zero);

    private static TaskItem New(int basePoints = 40, TaskBoardStatus status = TaskBoardStatus.Backlog, string? title = null) =>
        TaskItem.Create(Guid.NewGuid(), Guid.NewGuid(), 142, title ?? "Finalize venue floor plan", "Lay out the desks.",
            status, basePoints, Now.AddDays(7), ["Venue"], 100, Now, Guid.NewGuid());

    [Fact]
    public void Create_builds_the_reference_from_the_per_event_number()
    {
        var task = New();
        Assert.Equal("HTN-142", task.Reference);
        Assert.Equal(142, task.Number);
        Assert.True(task.IsOpen);
        Assert.Equal(["Venue"], task.Labels);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_task_needs_a_title(string title) =>
        Assert.Throws<DomainException>(() => New(title: title));

    [Fact]
    public void A_task_cannot_start_in_done_or_carry_impossible_points()
    {
        Assert.Throws<DomainException>(() => New(status: TaskBoardStatus.Done));
        Assert.Throws<DomainException>(() => New(basePoints: -5));
        Assert.Throws<DomainException>(() => New(basePoints: 1001));
    }

    [Fact]
    public void Labels_are_trimmed_deduplicated_and_capped()
    {
        Assert.Equal(["Venue", "AV"], TaskRules.CleanLabels([" Venue ", "venue", "AV", ""]));
        Assert.Throws<DomainException>(() => TaskRules.CleanLabels(["a", "b", "c", "d", "e", "f"]));
        Assert.Throws<DomainException>(() => TaskRules.CleanLabels([new string('x', 31)]));
        // All of them together are bounded too, mirroring ck_tasks_labels. The worst case the per-label rules allow
        // is five 30-character labels = 5 × 30 + 4 separators = 154 characters, which is inside the 160 the column
        // check enforces, so the combined limit only ever bites on a hand-written INSERT (see TaskGuards2Tests).
        var longest = Enumerable.Range(0, TaskItem.MaxLabels)
            .Select(i => new string((char)('a' + i), TaskItem.LabelMaxLength)).ToList();
        Assert.Equal(TaskItem.MaxLabels, TaskRules.CleanLabels(longest).Count);
        Assert.True(string.Join(",", longest).Length <= TaskItem.LabelsTotalMaxLength);
    }

    [Fact]
    public void Done_is_reached_by_approving_and_left_by_reopening()
    {
        var task = New();
        Assert.Throws<DomainException>(() => task.MoveTo(TaskBoardStatus.Done, 100, Now, null));

        task.MoveTo(TaskBoardStatus.Review, 100, Now, null);
        Assert.Equal(TaskBoardStatus.Review, task.Status);

        task.Approve(Now, Guid.NewGuid());
        Assert.Equal(TaskBoardStatus.Done, task.Status);
        Assert.NotNull(task.ApprovedAt);
        Assert.False(task.IsOpen);

        // Frozen while Done.
        Assert.Throws<DomainException>(() => task.SetBasePoints(80, Now, null));
        Assert.Throws<DomainException>(() => task.Update("Other", null, null, null, Now, null));
        Assert.Throws<DomainException>(() => task.MoveTo(TaskBoardStatus.InProgress, 100, Now, null));
        Assert.Throws<DomainException>(() => task.Approve(Now, null));
        Assert.Throws<DomainException>(() => task.Reopen(TaskBoardStatus.Done, "No.", Now, null));
        Assert.Throws<DomainException>(() => task.Reopen(TaskBoardStatus.Review, "", Now, null));

        task.Reopen(TaskBoardStatus.Review, "Booths block exit B.", Now, null);
        Assert.Equal(TaskBoardStatus.Review, task.Status);
        Assert.Null(task.ApprovedAt);
        Assert.True(task.IsOpen);
    }

    [Fact]
    public void Archiving_needs_a_reason_and_freezes_the_card()
    {
        var task = New();
        Assert.Throws<DomainException>(() => task.Archive("", hasLiveAwards: false, Now, null));
        // A card that still handed out live points can't leave the board: reopen it first.
        Assert.Throws<DomainException>(() => task.Archive("Duplicate card.", hasLiveAwards: true, Now, null));
        task.Archive("Duplicate card.", hasLiveAwards: false, Now, null);
        Assert.True(task.IsArchived);
        Assert.False(task.IsOpen);
        Assert.Equal("Duplicate card.", task.ArchiveReason);
        Assert.Throws<DomainException>(() => task.MoveTo(TaskBoardStatus.Review, 1, Now, null));
        Assert.Throws<DomainException>(() => task.Archive("Again.", hasLiveAwards: false, Now, null));

        task.Restore(Now, null);
        Assert.False(task.IsArchived);
        Assert.Null(task.ArchiveReason);
        Assert.Throws<DomainException>(() => task.Restore(Now, null));
    }

    [Fact]
    public void Updates_report_whether_anything_changed()
    {
        var task = New();
        Assert.False(task.Update("Finalize venue floor plan", "Lay out the desks.", task.DueAt, ["Venue"], Now, null));
        Assert.True(task.Update("Finalize floor plan", "Lay out the desks.", task.DueAt, ["Venue", "AV"], Now, null));
        Assert.False(task.SetBasePoints(40, Now, null));
        Assert.True(task.SetBasePoints(80, Now, null));
        Assert.Equal(80, task.BasePoints);
    }

    [Fact]
    public void A_volunteer_assignee_is_always_an_assistant()
    {
        var task = New();
        var volunteerId = Guid.NewGuid();
        Assert.Throws<DomainException>(() => TaskAssignee.ForVolunteer(task, volunteerId, TaskAssigneeRole.Lead, Now, null));
        var helper = TaskAssignee.ForVolunteer(task, volunteerId, TaskAssigneeRole.Assistant, Now, null);
        Assert.True(helper.IsVolunteer);
        Assert.False(helper.EarnsPoints);

        var person = TaskAssignee.ForPerson(task, Guid.NewGuid(), TaskAssigneeRole.Lead, Now, null);
        Assert.False(person.IsVolunteer);
        Assert.True(person.EarnsPoints);
    }

    [Fact]
    public void A_comment_is_edited_by_its_author_and_deleted_only_with_a_reason()
    {
        var comment = TaskComment.Create(Guid.NewGuid(), Guid.NewGuid(), "Booth 7 blocks exit B.", [Guid.NewGuid()], Now);
        Assert.False(comment.IsDeleted);
        Assert.True(comment.Edit("Booth 7 blocks exit B, can we move it?", Now.AddMinutes(5)));
        Assert.NotNull(comment.EditedAt);
        // The edit window closes.
        Assert.Throws<DomainException>(() => comment.Edit("Too late.", Now.AddDays(2)));

        Assert.Throws<DomainException>(() => comment.Delete("", Now, null));
        comment.Delete("Off topic.", Now.AddHours(1), Guid.NewGuid());
        Assert.True(comment.IsDeleted);
        Assert.Equal("Off topic.", comment.DeleteReason);
        Assert.Throws<DomainException>(() => comment.Edit("Back.", Now.AddHours(1)));
        Assert.Throws<DomainException>(() => comment.Delete("Again.", Now.AddHours(1), null));
    }

    [Fact]
    public void The_activity_log_only_accepts_known_kinds()
    {
        var taskId = Guid.NewGuid();
        var entry = TaskActivity.Record(taskId, Guid.NewGuid(), TaskActivityKinds.PointsChanged, "set base points to 40", Now);
        Assert.Equal("points_changed", entry.Kind);
        Assert.Throws<DomainException>(() => TaskActivity.Record(taskId, null, "whatever", "x", Now));
        Assert.Throws<DomainException>(() => TaskActivity.Record(taskId, null, TaskActivityKinds.Created, " ", Now));
        // Long sentences are trimmed instead of throwing.
        Assert.Equal(TaskActivity.TextMaxLength,
            TaskActivity.Record(taskId, null, TaskActivityKinds.Created, new string('x', 500), Now).Text.Length);
    }
}

public class PointsAwardTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 8, 0, 0, TimeSpan.Zero);

    private static TaskItem Task(int basePoints) =>
        TaskItem.Create(Guid.NewGuid(), Guid.NewGuid(), 1, "Book venue", null, TaskBoardStatus.Review, basePoints,
            null, null, 100, Now, null);

    [Theory]
    [InlineData(40, 1.0, 40.00)]
    [InlineData(40, 0.7, 28.00)]
    [InlineData(40, 0.3, 12.00)]
    [InlineData(15, 0.7, 10.50)]
    [InlineData(25, 0.3, 7.50)]
    [InlineData(35, 0.3, 10.50)]
    public void Points_are_base_times_multiplier_to_two_decimals(int basePoints, decimal multiplier, decimal expected)
    {
        var award = PointsAward.Create(Task(basePoints), Guid.NewGuid(), TaskAssigneeRole.Collab, multiplier, Now, null);
        Assert.Equal(expected, award.Points);
        Assert.Equal(basePoints, award.BasePoints);
        Assert.Equal(multiplier, award.Multiplier);
        Assert.True(award.IsLive);
    }

    [Fact]
    public void An_award_is_reversed_once_with_a_reason_and_never_deleted()
    {
        var award = PointsAward.Create(Task(40), Guid.NewGuid(), TaskAssigneeRole.Lead, 1.0m, Now, null);
        Assert.Throws<DomainException>(() => award.Reverse("", Now, null));
        award.Reverse("Reopened: booths block exit B.", Now.AddDays(1), Guid.NewGuid());
        Assert.False(award.IsLive);
        Assert.Equal("Reopened: booths block exit B.", award.ReversalReason);
        Assert.Throws<DomainException>(() => award.Reverse("Again.", Now.AddDays(2), null));
    }

    [Fact]
    public void A_multiplier_outside_the_allowed_range_is_refused() =>
        Assert.Throws<DomainException>(() => PointsAward.Create(Task(40), Guid.NewGuid(), TaskAssigneeRole.Lead, 3.0m, Now, null));
}

public class TaskAttachmentSignatureTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==");

    private static readonly byte[] Pdf = System.Text.Encoding.ASCII.GetBytes(
        "%PDF-1.7\n1 0 obj<</Type/Catalog>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n");

    [Fact]
    public void A_pdf_is_recognised_from_its_header_and_stored_byte_for_byte()
    {
        Assert.True(FileSignatures.IsPdf(Pdf));
        var detected = FileSignatures.DetectAttachment(Pdf);
        Assert.NotNull(detected);
        Assert.Equal("application/pdf", detected!.Value.ContentType);
        Assert.Equal(Pdf, detected.Value.Bytes);
    }

    [Fact]
    public void An_image_attachment_keeps_working_and_loses_its_metadata()
    {
        var detected = FileSignatures.DetectAttachment(Png);
        Assert.NotNull(detected);
        Assert.Equal("image/png", detected!.Value.ContentType);
        Assert.Equal(FileSignatures.StripMetadata(Png), detected.Value.Bytes);
    }

    [Theory]
    [InlineData("<html><body>hi</body></html>")]
    [InlineData("%PDFX-1.4 not really")]
    [InlineData("PK\u0003\u0004 zip")]
    [InlineData("")]
    public void Anything_else_is_refused(string content)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(content);
        Assert.False(FileSignatures.IsPdf(bytes));
        Assert.Null(FileSignatures.DetectAttachment(bytes));
    }

    [Fact]
    public void The_task_attachment_purpose_allows_five_megabytes() =>
        Assert.Equal(5 * 1024 * 1024, StoredFilePurposes.MaxBytesFor(StoredFilePurposes.TaskAttachment));
}
