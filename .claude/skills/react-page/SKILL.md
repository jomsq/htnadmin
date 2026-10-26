---
name: react-page
description: Recipe for building a frontend page or feature in the HTN React + TypeScript + Tailwind app (route, API hooks with TanStack Query, forms with react-hook-form + zod, loading/empty/error states, permission-aware UI, tests). Use whenever creating or changing screens from the design canvas.
---

# Frontend page recipe

Load `design-system` and `htn-domain` first. Find the screen in `docs/design.md` and match it: same labels, order and states.

## Structure
```
frontend/src/features/<area>/
  api.ts            # typed fetchers + TanStack Query hooks (useXQuery / useXMutation)
  schemas.ts        # zod schemas shared by forms and response parsing
  <Page>.tsx        # route component; composes sections
  components/       # feature-only components
  <Page>.test.tsx   # Vitest + Testing Library
```
Shared primitives (Button, Input, Select, Badge, Table, Tabs, SegmentedControl, Drawer, Dialog, Toast, EmptyState, Stat) live in `src/components/ui/`. Reuse them; don't restyle per page.

## Steps
1. **Route**
   - Add it in `src/app/router.tsx` under the right layout: `AppLayout` (sidebar), `EventLayout` (event header + tabs), `FinanceLayout` (board only) or `KioskLayout` (POS, check-in station, no sidebar).
   - Use lazy loading.
2. **Data**
   - `fetch` wrapper `api()` with `credentials: 'include'` and a CSRF header.
   - Parse responses with zod.
   - Query keys look like `['<area>', params]`. After a mutation, invalidate the precise keys.
3. **Forms**
   - react-hook-form + zodResolver, with visible `<label>` elements. Show errors under the field and set `aria-invalid`.
   - Disable submit while pending, and don't double-submit.
4. **States:** every list/page has loading skeleton, empty state (with the primary action), error state (retry), and success toast. Optimistic updates only for task card moves.
5. **Permissions**
   - `useCan('Policy.Name')` hides actions. The server still enforces them (see `permissions.md`).
   - Show view-only banners where the design does (Finance for board, Budget view only).
6. **Responsive:** sidebar collapses to a top bar under 768px; tables scroll inside `overflow-x-auto`; POS and Scanner are touch-first.
7. **Tests**
   - Render with MSW handlers. Assert the main action works and that the permission-hidden action is absent for the denied role.
   - Add a Playwright spec for flows listed in CLAUDE.md.

## Conventions
- TypeScript `strict`, no `any`. Components are function components; named exports; one component per file when it's over ~80 lines.
- Money is formatted with `formatPeso(n)` (`₱1,500`). Dates use `Intl.DateTimeFormat('en-PH')`. Participant IDs are shown in a monospace font.
- Icons are inline stroke SVGs (24px grid, stroke 1.8). No emoji in the UI.
