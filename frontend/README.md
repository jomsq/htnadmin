# HTN Admin frontend

React 19 + TypeScript (strict) + Vite, Tailwind CSS v4, React Router, TanStack Query, react-hook-form + zod.
UI follows the design canvas (`docs/design.md`) and the `design-system` skill.

## Scripts

| Command | What it does |
|---|---|
| `npm run dev` | Vite dev server on http://localhost:5173, proxying `/api` to the backend |
| `npm run build` | Type-check (`tsc -b`) and production build to `dist/` |
| `npm run preview` | Serve the production build |
| `npm run lint` | ESLint (typescript-eslint strict, react-hooks) |
| `npm run test` | Vitest + Testing Library + MSW, once |
| `npm run test:watch` | Vitest in watch mode |
| `npm run e2e` | Playwright specs in `e2e/` (needs the running stack; set `E2E_ADMIN_EMAIL` / `E2E_ADMIN_PASSWORD`, optional `E2E_BASE_URL`) |

## Environment

| Variable | Default | Purpose |
|---|---|---|
| `VITE_API_PROXY` | `http://localhost:8080` | Target for the dev server's `/api` proxy (e.g. `http://api:8080` inside docker compose) |

Copy `.env.example` to `.env` to override locally.

## Layout

```
src/app/            api() wrapper + ApiError, auth (AuthProvider, useAuth, useCan, RequireAuth), theme, router, layouts
src/components/ui/  design-system primitives (Button, Field, Dialog, Drawer, Toast, Table, SegmentedControl, ModeSwitch, ...)
src/features/<area> pages, api.ts (TanStack Query hooks), schemas.ts (zod), tests
src/test/           MSW server + render helpers
```

All requests use cookie auth (`credentials: 'include'`). Non-GET requests send `X-CSRF-TOKEN` from
`GET /api/auth/antiforgery` (cached, refreshed after login/logout and on an antiforgery 400).
Permission checks in the UI (`useCan`) only hide actions; the API is the real gate.
