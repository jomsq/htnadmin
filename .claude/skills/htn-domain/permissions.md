# Permissions matrix → authorization policies

Roles in Identity are `Board`, `Goon`, `Apprentice`. Scoped rights come from data:
- `CommitteeLead(eventId, committeeId)`
- `CFO`, a flag on one board member
- `FinanceAssistant`, a flag on at most one goon
- `PopUpCommittee(committeeId)`, set by admins
- `StoreTeam(popUpId)`

Members and volunteers have no login.

Each capability below is a **named policy** in `Htn.Api/Auth/Policies.cs`. Resource-scoped ones use an `IAuthorizationHandler` that loads the event, committee or pop-up. Every endpoint must call `.RequireAuthorization(Policies.X)`.

### How the committee-scoped policies work (Phase 4A)
`Committees.ManageMembers`, `Tasks.Manage`, `Tasks.Work` and `Tasks.ViewBoard` are one requirement
(`CommitteeScopedRequirement`) handled by `CommitteeScopedHandler`, which reads the caller's memberships from
`ICommitteeContext` (scoped, loaded once per request, active rows of active committees only).

| Resource passed to `AuthorizeAsync` | Decision (board members always pass) |
|---|---|
| `CommitteeScope(committeeId)` | manage: lead of that committee · work: on that committee · view: on that committee or a sibling committee of its event |
| `EventScope(eventId)` | manage: leads a committee of that event · work/view: on a committee of that event |
| `TaskScope(taskId)` | resolved by `ITaskScopeResolver` (Phase 4B); until then every task-scoped check fails closed |
| none, or the `HttpContext` that `.RequireAuthorization(...)` passes | **"may be allowed"**: leads something (manage) / is on something (work, view). This is the coarse route gate, and what `/api/auth/me` reports |

Routes carry the named policy as the coarse gate; where the committee is only known after the row is loaded, the
endpoint re-runs the same policy against the real resource and returns 403 (`ScopedAuthorization.DenyAsync`).
Because the flat list in `/api/auth/me` can only say "may be allowed", every payload that drives buttons carries
explicit per-resource flags (`canManage`, `canCreateCommittee`, `canManageVolunteers`, later `canMove`, `canEdit`,
`canWork`, `canApprove`, `canComment`).

Creating, renaming, re-leading and deactivating a **committee** is board-only (a lead manages the people on their own
committee, not the committee itself), so those routes add a second board-only gate next to the scoped policy.
A committee **lead must be an active goon**; committee **members need an active login** (Board, Goon or Apprentice).
Volunteers have no login: they are per-event records managed by the board or the lead of the committee they are on.

| Policy | Board (admin) | Committee lead | Goon | Apprentice |
|---|---|---|---|---|
| `People.Manage` (create accounts, levels, promotions, positions) | ✓ | — | — | — |
| `People.View` | ✓ | view | view | view |
| `Events.Manage` (event, tickets, vouchers, settings) | ✓ | — | — | — |
| `Events.View` (events list and details; Drafts and vouchers only with `Events.Manage`) | ✓ | ✓ | ✓ | ✓ |
| `Committees.ManageMembers` (add/remove members and volunteers; create/rename/deactivate a committee and set its lead are board-only) | ✓ | own committee | — | — |
| `Tasks.Manage` (create, assign, set points, approve to Done) | ✓ | own committee | — | — |
| `Tasks.Work` (move own cards, comment, checklist) | ✓ | ✓ | own tasks | own tasks |
| `Tasks.ViewBoard` | ✓ | ✓ | events they're on | events they're on |
| `Registrations.Manage` (manual registration, mark paid, cancel, resend) | ✓ | — | — | — |
| `CheckIn.Scan` | ✓ | ✓ | ✓ | ✓ |
| `Sponsors.Manage`, `Leads.Manage`, `Invitations.Manage`, `Directory.Manage` | ✓ | — | — | — |
| `Directory.View` (contacts only) | ✓ | ✓ | — | — |
| `Speakers.Manage` (send / revoke links, view travel) | ✓ | Programs lead | — | — |
| `Suppliers.Manage` (hire, prices, TIN, bank) | ✓ | — | — | — |
| `Suppliers.View` (contacts, docs, reviews) | ✓ | ✓ | — | — |
| `Suppliers.Rate` | ✓ | own committee's suppliers | — | — |
| `Merch.Manage` (stock, discounts) | ✓ | — | — | — |
| `Merch.Sell` (event POS) | ✓ | own committee | if assigned | if assigned |
| `PopUps.Manage` (create / run pop-up) | ✓ | if committee allowed | — | — |
| `PopUps.Sell` | ✓ | if committee allowed | if on store team | if on store team |
| `Finance.View` (ledger, budgets, cashflow) | ✓ | — | — | — |
| `Finance.Post` (money in/out, transfers, void, reconcile, close month, budgets) | **CFO only** | — | finance assistant: add only, stays Pending | — |
| `Finance.Approve` | **CFO only** | — | — | — |
| `Finance.Request` (expense requests) | ✓ | — | — | — |
| `Settings.Manage` (points multipliers, payment accounts, email branding, org theme) | ✓ | — | — | — |
| `Settings.Personal` (own mode / text size / density) | ✓ | ✓ | ✓ | ✓ |

## Rules
- Denials return 403. Integration tests must check at least one denied role for every endpoint group.
- Finance endpoints also return 404 to non-board users, so the module's existence isn't revealed.
- Prices, TIN and bank fields are removed from responses for non-board users. Do this at the DTO projection, not in the UI.
