# HTN Admin

Admin system for **HackTheNorth.PH**, a non-stock, non-profit corporation. It covers the organization (board, goons, apprentices, members), events (committees, task board with points, agenda, speakers), registrations and payments with QR tickets, sponsors, partners, directory and invitations, suppliers, merch POS and pop-up stores, and board-only finance.

- **Design (source of truth for UI):** https://claude.ai/artifact/2N4QBGtFAZvLyGdqXxgVZV — screen index in `docs/design.md`
- **Business rules:** `.claude/skills/htn-domain/` — read before touching any feature.

## Stack
| Layer | Choice |
|---|---|
| Backend | .NET 10 (LTS), ASP.NET Core Minimal APIs, EF Core + Npgsql, ASP.NET Core Identity (cookie auth), FluentValidation |
| Database | PostgreSQL 17 |
| Frontend | React + TypeScript + Vite, Tailwind CSS v4, React Router, TanStack Query, react-hook-form + zod |
| Email | Mailtrap Email API (sandbox in dev, live in prod) through an outbox |
| Tests | xUnit + Testcontainers (Postgres), Vitest + Testing Library, Playwright e2e |
| Runtime | Docker / docker compose; CI on GitHub Actions |

## Layout (target)
```
backend/
  src/Htn.Api/            # Program.cs, endpoint groups, auth, DI
  src/Htn.Domain/         # entities, value objects, enums, domain rules (no EF/ASP.NET refs)
  src/Htn.Infrastructure/ # DbContext, EF configurations, migrations, Mailtrap, outbox, storage
  tests/Htn.Api.Tests/    # integration tests (Testcontainers)
  tests/Htn.Domain.Tests/ # unit tests
frontend/
  src/app/                # router, providers, layout (sidebar, event tabs)
  src/features/<area>/    # pages, components, api hooks per feature area
  src/components/ui/      # design-system primitives
docker-compose.yml        # api, web, db
docs/
```
Feature areas (same names in backend and frontend): `organization`, `events`, `tasks`, `registrations`, `checkin`, `sponsors`, `directory`, `speakers`, `merch`, `finance`, `settings`.

## Commands
```bash
docker compose up -d --build                 # whole stack: web http://localhost:5173, api http://localhost:8080, db 5432
dotnet build backend && dotnet test backend  # backend build + tests
dotnet ef migrations add <Name> -p backend/src/Htn.Infrastructure -s backend/src/Htn.Api
cd frontend && npm run dev | npm run build | npm run test | npm run lint | npx playwright test
```

## Non-negotiables
1. **Authorization is enforced on the server.** Every endpoint needs a named policy from `.claude/skills/htn-domain/permissions.md`. Hiding a button in the UI is never enough.
2. **Audit everything sensitive.** Any change to money, permissions, levels/positions, registrations status, vouchers or speaker links writes an append-only `AuditEntry` (who, what, before/after, when).
3. **No hard deletes** for financial, registration, sponsor or audit data. Use void or cancel, and always record a reason.
4. **Money** is stored as `decimal(12,2)` in PHP, never float. Balances are derived from posted ledger entries.
5. **Secrets come only from environment variables** (`MAILTRAP_API_TOKEN`, connection strings). Never commit them; `.env` is git-ignored and `.env.example` is committed.
6. **Personal data:** collect only what the design asks for. Store Data Privacy Act consent with a timestamp. Mask emails in lists where the design does.
7. **IDs shown to the public** (participant IDs, voucher codes, speaker tokens, QR payloads) are random or signed. Never use sequential database IDs.
8. **UI follows the design canvas and the `design-system` skill:** brand #B71C1C, Tailwind gray scale, light/dark/system modes, WCAG AA, 44px touch targets, real `<button>`/`<label>`.
9. **Each feature ships with tests:** an integration test per endpoint group, covering authorization denial, plus a Playwright test for the critical flows (register → pay → ticket → check-in, POS sale, finance posting).

## Working agreements
- Build in **vertical slices**: one feature area end to end (migration → API → UI → tests). Use the `dotnet-slice` and `react-page` skills.
- Use the agents in `.claude/agents/`. Run `security-reviewer` before merging anything that touches auth, finance, registrations or speaker links.
- Keep PRs small. Never push to `main` directly.
