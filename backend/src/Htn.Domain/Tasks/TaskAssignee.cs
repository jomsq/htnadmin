using Htn.Domain.Common;

namespace Htn.Domain.Tasks;

/// <summary>
/// Someone on a task: a person with a login <b>or</b> an event volunteer, never both (<c>ck_task_assignees_who</c>).
/// Several people may hold the same role. A volunteer is always <see cref="TaskAssigneeRole.Assistant"/>, has no login
/// and never earns points, so no <see cref="PointsAward"/> row is ever written for one.
/// </summary>
public sealed class TaskAssignee
{
    private TaskAssignee() { }

    public Guid Id { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid? PersonId { get; private set; }
    public Guid? VolunteerId { get; private set; }
    public TaskAssigneeRole Role { get; private set; }
    public DateTimeOffset AddedAt { get; private set; }
    public Guid? AddedById { get; private set; }

    public bool IsVolunteer => VolunteerId is not null;

    /// <summary>Volunteers never earn points (domain rule, also enforced by the awarding query).</summary>
    public bool EarnsPoints => PersonId is not null;

    public static TaskAssignee ForPerson(TaskItem task, Guid personId, TaskAssigneeRole role, DateTimeOffset at, Guid? actorId)
    {
        ArgumentNullException.ThrowIfNull(task);
        task.EnsureEditable();
        if (personId == Guid.Empty) throw new DomainException("A person is required.");
        if (!Enum.IsDefined(role)) throw new DomainException("Unknown task role.");
        return new TaskAssignee
        {
            Id = Guid.CreateVersion7(at), TaskId = task.Id, PersonId = personId, Role = role, AddedAt = at, AddedById = actorId,
        };
    }

    public static TaskAssignee ForVolunteer(TaskItem task, Guid volunteerId, TaskAssigneeRole role, DateTimeOffset at, Guid? actorId)
    {
        ArgumentNullException.ThrowIfNull(task);
        task.EnsureEditable();
        if (volunteerId == Guid.Empty) throw new DomainException("A volunteer is required.");
        if (role != TaskAssigneeRole.Assistant)
            throw new DomainException("A volunteer can only help as an Assistant.");
        return new TaskAssignee
        {
            Id = Guid.CreateVersion7(at), TaskId = task.Id, VolunteerId = volunteerId, Role = role, AddedAt = at, AddedById = actorId,
        };
    }
}
