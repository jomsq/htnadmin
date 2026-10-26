using Htn.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;

namespace Htn.Api.Auth;

/// <summary>Every capability from <c>.claude/skills/htn-domain/permissions.md</c> as a named policy.</summary>
public static class Policies
{
    public const string PeopleManage = "People.Manage";
    public const string PeopleView = "People.View";
    public const string EventsManage = "Events.Manage";
    public const string EventsView = "Events.View";
    public const string CommitteesManageMembers = "Committees.ManageMembers";
    public const string TasksManage = "Tasks.Manage";
    public const string TasksWork = "Tasks.Work";
    public const string TasksViewBoard = "Tasks.ViewBoard";
    public const string RegistrationsManage = "Registrations.Manage";
    public const string CheckInScan = "CheckIn.Scan";
    public const string SponsorsManage = "Sponsors.Manage";
    public const string LeadsManage = "Leads.Manage";
    public const string InvitationsManage = "Invitations.Manage";
    public const string DirectoryManage = "Directory.Manage";
    public const string DirectoryView = "Directory.View";
    public const string SpeakersManage = "Speakers.Manage";
    public const string SuppliersManage = "Suppliers.Manage";
    public const string SuppliersView = "Suppliers.View";
    public const string SuppliersRate = "Suppliers.Rate";
    public const string MerchManage = "Merch.Manage";
    public const string MerchSell = "Merch.Sell";
    public const string PopUpsManage = "PopUps.Manage";
    public const string PopUpsSell = "PopUps.Sell";
    public const string FinanceView = "Finance.View";
    public const string FinancePost = "Finance.Post";
    public const string FinanceApprove = "Finance.Approve";
    public const string FinanceRequest = "Finance.Request";
    public const string SettingsManage = "Settings.Manage";
    public const string SettingsPersonal = "Settings.Personal";

    public static readonly IReadOnlyList<string> All =
    [
        PeopleManage, PeopleView, EventsManage, EventsView, CommitteesManageMembers, TasksManage, TasksWork, TasksViewBoard,
        RegistrationsManage, CheckInScan, SponsorsManage, LeadsManage, InvitationsManage, DirectoryManage, DirectoryView,
        SpeakersManage, SuppliersManage, SuppliersView, SuppliersRate, MerchManage, MerchSell, PopUpsManage, PopUpsSell,
        FinanceView, FinancePost, FinanceApprove, FinanceRequest, SettingsManage, SettingsPersonal,
    ];

    /// <summary>Board-only, for routes that need a second, coarser gate next to a resource-scoped policy.</summary>
    public static readonly AuthorizationPolicy BoardOnly =
        new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireRole(Roles.Board).Build();

    /// <summary>
    /// Phase 1 implements People.* and Settings.Personal; Phase 3 adds Events.View and CheckIn.Scan (any signed-in
    /// Board/Goon/Apprentice); Phase 4A adds the four resource-scoped committee/task policies. Everything else is
    /// registered as Board-only (Finance.Post/Approve as CFO-only) until its slice adds the scoped handlers.
    /// </summary>
    public static AuthorizationBuilder AddHtnPolicies(this AuthorizationBuilder auth)
    {
        auth.SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        auth.AddPolicy(PeopleView, p => p.RequireAuthenticatedUser().RequireRole(Roles.Board, Roles.Goon, Roles.Apprentice));
        auth.AddPolicy(PeopleManage, p => p.RequireAuthenticatedUser().RequireRole(Roles.Board));
        auth.AddPolicy(SettingsPersonal, p => p.RequireAuthenticatedUser());
        auth.AddPolicy(EventsView, p => p.RequireAuthenticatedUser().RequireRole(Roles.Board, Roles.Goon, Roles.Apprentice));
        // Every staff role works the check-in desk (committee leads are goons).
        auth.AddPolicy(CheckInScan, p => p.RequireAuthenticatedUser().RequireRole(Roles.Board, Roles.Goon, Roles.Apprentice));

        auth.AddPolicy(FinancePost, p => p.RequireAuthenticatedUser().RequireRole(Roles.Board).RequireClaim(HtnClaimTypes.Cfo, "true"));
        auth.AddPolicy(FinanceApprove, p => p.RequireAuthenticatedUser().RequireRole(Roles.Board).RequireClaim(HtnClaimTypes.Cfo, "true"));

        // Resource-scoped (see CommitteeScopedHandler). The role list is the coarse gate: Committees.ManageMembers and
        // Tasks.Manage can only ever be held by the board or a committee lead, who is always a goon. The requirement
        // then decides per committee / event / task, and with no resource it answers "may be allowed" for
        // /api/auth/me, which is why the payloads carry explicit canManage flags.
        auth.AddPolicy(CommitteesManageMembers, p => p.RequireAuthenticatedUser().RequireRole(Roles.Board, Roles.Goon)
            .AddRequirements(new CommitteeScopedRequirement(CommitteeAccess.ManageMembers)));
        auth.AddPolicy(TasksManage, p => p.RequireAuthenticatedUser().RequireRole(Roles.Board, Roles.Goon)
            .AddRequirements(new CommitteeScopedRequirement(CommitteeAccess.TasksManage)));
        auth.AddPolicy(TasksWork, p => p.RequireAuthenticatedUser().RequireRole(Roles.Board, Roles.Goon, Roles.Apprentice)
            .AddRequirements(new CommitteeScopedRequirement(CommitteeAccess.TasksWork)));
        auth.AddPolicy(TasksViewBoard, p => p.RequireAuthenticatedUser().RequireRole(Roles.Board, Roles.Goon, Roles.Apprentice)
            .AddRequirements(new CommitteeScopedRequirement(CommitteeAccess.ViewBoard)));

        string[] implemented =
        [
            PeopleView, PeopleManage, SettingsPersonal, EventsView, CheckInScan, FinancePost, FinanceApprove,
            CommitteesManageMembers, TasksManage, TasksWork, TasksViewBoard,
        ];
        foreach (var name in All.Except(implemented))
            auth.AddPolicy(name, p => p.RequireAuthenticatedUser().RequireRole(Roles.Board));

        return auth;
    }
}
