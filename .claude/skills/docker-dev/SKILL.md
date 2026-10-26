---
name: docker-dev
description: Run, reset and debug the HTN local stack with Docker Compose (api .NET 10, web React/Vite, Postgres 17) — env files, migrations, seed data, logs, common fixes. Use when starting the app, running migrations, resetting the database, or when containers fail.
---

# Local stack

## Services (`docker-compose.yml`)
| Service | Image / build | Port | Notes |
|---|---|---|---|
| `db` | `postgres:17-alpine` | 5432 | volume `pgdata`, healthcheck `pg_isready` |
| `api` | `backend/Dockerfile` (multi-stage, `mcr.microsoft.com/dotnet/sdk:10.0` → `aspnet:10.0`, non-root) | 8080 | depends on healthy `db`; applies migrations on start in Development only |
| `web` | `frontend/Dockerfile` (dev: Vite; prod: build → nginx) | 5173 | proxies `/api` to `api:8080` |

Secrets and config come from `.env`, which is git-ignored. Copy `.env.example` (committed) to `.env`.
```
POSTGRES_PASSWORD=change-me
ConnectionStrings__Db=Host=db;Database=htn;Username=htn;Password=change-me
MAILTRAP_API_TOKEN=
MAILTRAP_MODE=sandbox
MAILTRAP_INBOX_ID=
TICKET_SIGNING_KEY=        # empty in Development (built-in dev key); outside Development 32+ random bytes, base64
```

Production notes (details in the README):
- `PUBLIC_BASE_URL` must be an absolute `https://` URL outside Development, and published dev signing keys are refused.
- `TRUSTED_PROXY_CIDR` (web): the TLS terminator in front of nginx, for nginx's realip module. Default `127.0.0.1/32`.
- Run the API as the non-owner role `htn_app` (migrations as the owner); the REVOKEs in the migrations target it. TODO: the role isn't created by the stack yet.

## Everyday commands
```bash
cp .env.example .env                       # first time only
docker compose up -d --build               # start everything
docker compose logs -f api                 # follow API logs
docker compose exec db psql -U htn htn     # SQL shell
docker compose run --rm api dotnet ef database update   # apply migrations manually
docker compose run --rm api dotnet Htn.Api.dll --seed   # seed demo data (sample event, people, sponsors)
docker compose down -v                     # stop and DELETE the database volume
```

## Seed data
Mirror the design canvas samples: HTN Con 2026, committees, the people shown (Alex Reyes board admin, Mika Soriano Logistics lead, Leo Mendoza CFO, Gia Pascual finance assistant…), tickets, vouchers, sponsors and pop-ups. Dev accounts get password `Dev-Password-123!` and are only seeded when `ASPNETCORE_ENVIRONMENT=Development`.

## Common fixes
- **api exits with a connection refused error:** db isn't healthy yet. Check `docker compose ps`, then the db logs.
- **Port in use:** change the host port in `docker-compose.override.yml`. Don't edit the base file.
- **Migration drift:** `docker compose down -v` then `up` (dev only). Never do this against shared data.
- **Emails not arriving:** check `MAILTRAP_MODE=sandbox` and the inbox ID, then look at failed outbox rows: `select * from outbox_messages where status='Failed'`.
