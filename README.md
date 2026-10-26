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

Dev login: `alex@hackthenorth.ph` / `Dev-Password-123!`. Dev accounts are seeded **only** when `ASPNETCORE_ENVIRONMENT=Development` **and** `Seed__Dev=true` (the dev compose default). The api and web ports are bound to `127.0.0.1` only.

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

### Production settings

Outside Development the API refuses to start unless these are set (see `docker-compose.prod.yml`):

- `MAILTRAP_API_TOKEN` (or `Email__AllowLoggingSender=true` to only log emails, with masked recipients).
- `TICKET_SIGNING_KEY`: the HMAC key that signs ticket QR codes, at least 32 bytes (`openssl rand -base64 48`). Keep it secret: anyone with it can forge tickets. Rotating it invalidates every QR already sent, so resend tickets afterwards. The dev compose file sets a published dev-only value; `docker-compose.prod.yml` refuses to start without one.
- Optional: `MAIL_REPLY_TO`, a monitored inbox that receives replies (proof of payment for events in reply-by-email mode).
- `DataProtection__CertificatePath` (+ `DataProtection__CertificatePassword`): a PFX with a private key, mounted into the api container, used to encrypt the Data Protection keys stored in Postgres. `DataProtection__AllowUnprotectedKeys=true` skips this; use it only for throwaway local runs. This one is enforced in Production only.
- Forwarded headers: nginx overwrites `X-Forwarded-For` with the connecting address, and the API's auth rate limits key on it. Set `ForwardedHeaders__KnownNetworks` (CIDRs) or `ForwardedHeaders__KnownProxies` (IPs), comma-separated, to the nginx container's network. `ForwardedHeaders__ForwardLimit` defaults to 1. If a TLS terminator sits in front of nginx, enable nginx's realip module for it, otherwise every client shares one rate-limit bucket.

Put machine-specific changes (other ports, file-watch polling) in `docker-compose.override.yml`, which Compose loads automatically. Don't edit the base file for these.

Without Docker: `dotnet build backend && dotnet test backend`, then `cd frontend && npm ci && npm run dev`.

## Docs

- [CLAUDE.md](CLAUDE.md): stack, layout, commands, and the non-negotiable rules
- [docs/design.md](docs/design.md): screen index for the UI design
- CI: [.github/workflows/ci.yml](.github/workflows/ci.yml) (backend build/test, frontend lint/test/build, image build)
