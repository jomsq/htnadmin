using Htn.Domain.Organization;

namespace Htn.Infrastructure.Identity;

/// <summary>Identity roles. They mirror the login levels of <see cref="PersonLevel"/>.</summary>
public static class Roles
{
    public const string Board = "Board";
    public const string Goon = "Goon";
    public const string Apprentice = "Apprentice";

    public static readonly Guid BoardId = new("0192f0a0-0000-7000-8000-000000000001");
    public static readonly Guid GoonId = new("0192f0a0-0000-7000-8000-000000000002");
    public static readonly Guid ApprenticeId = new("0192f0a0-0000-7000-8000-000000000003");

    public static string For(PersonLevel level) => level switch
    {
        PersonLevel.Board => Board,
        PersonLevel.Goon => Goon,
        PersonLevel.Apprentice => Apprentice,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Members have no login."),
    };
}

public static class HtnClaimTypes
{
    public const string PersonId = "htn:person_id";
    public const string Cfo = "htn:cfo";
    public const string FinanceAssistant = "htn:finance_assistant";
    public const string TwoFactorEnabled = "htn:2fa";
}
