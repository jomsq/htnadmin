# HTN Admin

The admin system for **HackTheNorth.PH**, a non-stock, non-profit corporation. It covers the organization (board, goons, apprentices, members), events, registrations and payments, sponsors, the directory, merch, and board-only finance.

Stack: .NET 10 API, React + TypeScript + Vite web app, PostgreSQL 17, all run with Docker Compose.

## Quick start

```bash
cp .env.example .env          # first time only; fill in secrets as needed
docker compose up -d --build  # db, api, web
docker compose ps             # wait until all three are "healthy"
```

- Web: http://localhost:5173
- API: http://localhost:8080 (health: http://localhost:8080/health)
- Postgres: localhost:5432 (database `htn`, user `htn`)

Dev login: `alex@hackthenorth.ph` / `Dev-Password-123!`. Dev accounts are seeded **only** when `ASPNETCORE_ENVIRONMENT=Development`.

Without `MAILTRAP_API_TOKEN` the API logs outgoing emails instead of sending them. To receive invites in the Mailtrap sandbox, set `MAILTRAP_API_TOKEN` and `MAILTRAP_INBOX_ID` in `.env`.

## Useful commands

```bash
docker compose logs -f api                  # follow API logs
docker compose exec db psql -U htn htn      # SQL shell
docker compose up -d --build -V             # rebuild and refresh node_modules after package.json changes
docker compose down -v                      # stop and DELETE the database volume (dev only)

# Production-like run: nginx serves the built SPA on http://localhost and proxies /api
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build
```

Put machine-specific changes (other ports, file-watch polling) in `docker-compose.override.yml`, which Compose loads automatically. Don't edit the base file for these.

Without Docker: `dotnet build backend && dotnet test backend`, then `cd frontend && npm ci && npm run dev`.

## Docs

- [CLAUDE.md](CLAUDE.md): stack, layout, commands, and the non-negotiable rules
- [docs/design.md](docs/design.md): screen index for the UI design
- CI: [.github/workflows/ci.yml](.github/workflows/ci.yml) (backend build/test, frontend lint/test/build, image build)
