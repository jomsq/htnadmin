# Permissions matrix → authorization policies

Roles in Identity are `Board`, `Goon`, `Apprentice`. Scoped rights come from data:
- `CommitteeLead(eventId, committeeId)`
- `CFO`, a flag on one board member
- `FinanceAssistant`, a flag on at most one goon
- `PopUpCommittee(committeeId)`, set by admins
- `StoreTeam(popUpId)`

Members and volunteers have no login.

Each capability below is a **named policy** in `Htn.Api/Auth/Policies.cs`. Resource-scoped ones use an `IAuthorizationHandler` that loads the event, committee or pop-up. Every endpoint must call `.RequireAuthorization(Policies.X)`.

| Policy | Board (admin) | Committee lead | Goon | Apprentice |
|---|---|---|---|---|
| `People.Manage` (create accounts, levels, promotions, positions) | ✓ | — | — | — |
| `People.View` | ✓ | view | view | view |
| `Events.Manage` (event, tickets, vouchers, settings) | ✓ | — | — | — |
| `Committees.ManageMembers` | ✓ | own committee | — | — |
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
