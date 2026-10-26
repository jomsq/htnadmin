# Design index

The design canvas, "HTN Admin Dashboard", is the source of truth for every screen: https://claude.ai/artifact/2N4QBGtFAZvLyGdqXxgVZV

Each canvas frame has a number. The route and feature area below are the target for the React app (`frontend/src/features/<area>`).

| # | Screen | Route | Area | Layout |
|---|---|---|---|---|
| 00 | Sign in, invite activation, password reset (no sign-up) | `/login`, `/invite/:token`, `/reset/:token` | auth | blank |
| 01 | Dashboard | `/` | dashboard | app |
| 02 | Organization: people, positions, create account, membership | `/people`, `/people/positions` | organization | app |
| 03 | Roles & permissions | `/settings/permissions` | settings | app |
| 04 | Events list | `/events` | events | app |
| 05 | Event details, tickets, vouchers, registration & payment, speaker travel | `/events/:id/details` | events | event |
| 06 | Committees | `/events/:id/committees` | events | event |
| 07 | Task board (kanban) | `/events/:id/tasks` | tasks | event |
| 08 | Task detail & points split | `/events/:id/tasks/:taskId` (dialog) | tasks | event |
| 09 | Registrations & payments (hash IDs, mark paid) | `/events/:id/registrations` | registrations | event |
| 10 | Public registration form (mobile) | `/r/:eventSlug` | registrations (public) | public |
| 11 | Email templates: billing & ticket | `/events/:id/emails` | registrations | event |
| 12 | Check-in scanner (phone) | `/events/:id/check-in/scan` | checkin | kiosk |
| 12b | Check-in station (laptop + handheld scanner / webcam) | `/events/:id/check-in/desk` | checkin | kiosk |
| 13 | Sponsors & tiers, partners, logo/website, individuals | `/events/:id/sponsors` | sponsors | event |
| 14 | Leaderboard | `/leaderboard` | tasks | app |
| 15 | Settings: appearance, points, payment accounts, branding | `/settings` | settings | app |
| 16 | Agenda (programme) | `/events/:id/agenda` | events | event |
| 17 | Leads: sponsors & speakers | `/events/:id/leads` | sponsors | event |
| 18 | Directory: organizations (global) | `/directory` | directory | app |
| 18b | Directory: suppliers (global) | `/directory/suppliers` | directory | app |
| 18c | Rate your suppliers (committee lead) | `/tasks/rate-suppliers/:eventId` | directory | app |
| 19 | Invitations per event | `/events/:id/invitations` | directory | event |
| 20 | Merch: inventory, discounts, sales | `/events/:id/merch` | merch | event |
| 21 | Merch POS (also membership sales) | `/events/:id/pos`, `/store/popups/:id/pos` | merch | kiosk |
| 21b | Merch store: leftover stock & pop-ups | `/store` | merch | app |
| 22 | Speakers: confirmation & travel | `/events/:id/agenda/speakers` | speakers | event |
| 23 | Speaker confirmation link (public, private token + OTP) | `/s/:token` | speakers (public) | public |
| 24 | Finance: cashflow, ledger, balancing, audit (board only) | `/finance` | finance | finance |
| 25 | Finance: event budget vs actual, suppliers for the event | `/finance/budgets/:eventId` | finance | finance |
| 26 | Finance: expense requests | `/finance/requests` | finance | finance |

## Suggested build order (vertical slices)
1. Foundation: Docker stack, auth (admin-created accounts, invites, 2FA for board), app shell, design-system primitives, audit log, outbox + Mailtrap.
2. Organization (people, levels, positions) and Settings (appearance, points, payment accounts).
3. Events, tickets, vouchers → public registration → billing email → mark paid → ticket + QR → check-in (phone + desk).
4. Committees, task board, points, leaderboard.
5. Finance core: funds, ledger, auto-posting, balancing, audit, requests, budgets.
6. Sponsors, partners, leads, speakers (links + OTP, plus-one vouchers), agenda.
7. Directory, invitations, suppliers and ratings.
8. Merch, POS, membership sales, merch store and pop-ups.
