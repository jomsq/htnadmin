---
name: design-system
description: "HTN visual design system for the React + Tailwind v4 frontend — brand color #B71C1C, Tailwind gray scale, Catalyst-style components, light/dark/system themes, spacing, typography, status badges, tables, accessibility rules. Use whenever writing or reviewing any UI so screens match the design canvas and never look like generic AI templates."
---

# HTN design system

Reference: the design canvas (https://claude.ai/artifact/2N4QBGtFAZvLyGdqXxgVZV). The look follows Tailwind's application kit (Catalyst): calm grays, one brand color, shadow-ring cards, small square-ish badges.

## Tokens (`src/index.css`, Tailwind v4 `@theme`)
```css
@import "tailwindcss";
@custom-variant dark (&:where([data-theme=dark], [data-theme=dark] *));
@theme {
  --font-sans: "Inter", ui-sans-serif, system-ui, sans-serif;
  --color-brand-50:  #FEF2F2;
  --color-brand-100: #FEE2E2;
  --color-brand-600: #B71C1C;   /* primary: buttons, active tab underline, links */
  --color-brand-700: #8E1616;   /* hover, active text */
  --color-brand-300: #F87171;   /* links/active text in dark mode */
}
```
- **Surfaces:** page `bg-gray-50` / dark `bg-gray-950`; cards `bg-white` / dark `bg-gray-900`.
- **Text:** body `text-gray-900` / dark `text-gray-50`; secondary `text-gray-600` / dark `text-gray-400`. Nothing lighter than gray-500 for text.
- **Brand color in the org theme is admin-configurable.** Expose it as a CSS variable `--brand` set from settings, and keep the brand-* utilities mapped to it.
- **Theme modes:** Light / System / Dark.
  - Set `data-theme` on `<html>` from user settings. System follows `prefers-color-scheme`.
  - The switch is an icon-only segmented control (sun, monitor, moon) with `aria-label` and a tooltip.
- **User settings** also include text size (Default / Large: root 16px → 18px) and density (Comfortable / Compact: row padding `py-3` → `py-2`).

## Components (in `src/components/ui/`)
- **Card:** `rounded-lg bg-white shadow-sm ring-1 ring-gray-900/5 dark:bg-gray-900 dark:ring-white/10`. **Never** a colored left/top border stripe.
- **Button:**
  - primary: `rounded-md bg-brand-600 px-4 h-10 text-sm font-semibold text-white hover:bg-brand-700`
  - secondary: `ring-1 ring-gray-300 bg-white text-gray-700`
  - destructive text: `text-red-700`
- **Badge:** `rounded-md px-2 py-0.5 text-xs font-medium`, as tint bg + 700 text.
  - green = paid / confirmed / balanced
  - yellow = pending / needs check
  - blue = info / in progress
  - red = error / over / missing
  - gray = neutral / ended
- **Tabs:** event sections use an underline style (`border-b-2 border-brand-600 text-brand-700`). Sub-views use a segmented control (`bg-gray-100 p-1 rounded-lg`, selected `bg-white shadow-sm`).
- **Filter chips:** `rounded-md h-9 px-3 ring-1`. Selected: `bg-brand-50 text-brand-700 ring-brand-600`.
- **Table:**
  - header `bg-gray-50 text-xs font-semibold text-gray-600`; rows `border-t border-gray-100`
  - numbers right-aligned with `tabular-nums`
  - wrapped in `overflow-x-auto`
- **Sidebar:**
  - item `h-10 rounded-md px-3 text-sm font-medium text-gray-700`; active `bg-gray-100 text-brand-700`
  - lock icon on Finance
  - the mode switch sits above the user block
- **Drawer/side panel** for record details (person, supplier, request). **Dialog** for confirmations; give void/cancel a reason textarea.

## Patterns from the canvas
- Page header: H1 `text-2xl font-semibold`, a one-line description, the primary action at right.
- KPI row: `grid auto-fit minmax(200px,1fr)` cards (label, value `text-2xl`, sub).
- Kanban: columns `bg-gray-100 rounded-lg p-3`; cards are white ring cards.
- Kiosk screens (POS, check-in station, scanner) have a large result card: green / yellow / red, a 72px icon circle and a 28px title.

## Accessibility (must pass)
- WCAG 2.2 AA contrast in both themes. Status colors also differ in lightness, and every color signal carries text.
- Touch targets ≥ 44px on mobile/kiosk screens; visible focus ring `focus-visible:outline-2 outline-brand-600 outline-offset-2`.
- Real `<button>`, `<a href>`, `<label>`. Icon-only buttons need `aria-label`. Live results use `aria-live`.
- No emoji. Icons are inline stroke SVGs.

## Don't
Gradient washes, colored side stripes on cards, pill-everything, glassmorphism, oversized hero text in admin screens, purple/indigo defaults.
