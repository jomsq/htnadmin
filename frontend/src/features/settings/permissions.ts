/**
 * Roles & permissions matrix as shown on the canvas (frame 03), in plain language.
 * It summarises .claude/skills/htn-domain/permissions.md; the server enforces the named policies.
 * Keep both in sync when the matrix changes.
 */
import type { BadgeColor } from '@/components/ui/Badge'

export type RoleColumn = { name: string; login: string; color: BadgeColor }

export const roleColumns: readonly RoleColumn[] = [
  { name: 'Admin · Board', login: 'Full login', color: 'blue' },
  { name: 'Committee lead', login: 'Goon, per event', color: 'brand' },
  { name: 'Goon', login: 'Limited login', color: 'brand' },
  { name: 'Apprentice', login: 'Limited login', color: 'teal' },
  { name: 'Member · Volunteer', login: 'No login', color: 'gray' },
]

/** Full = create, edit, delete; Own = their committee or own tasks; View = read only; None = hidden. */
export type AccessLevel = 'full' | 'own' | 'view' | 'none'
export type Access = { level: AccessLevel; text: string }

const defaultText: Record<AccessLevel, string> = { full: 'Full', own: 'Own', view: 'View', none: 'None' }
const codes: Record<string, AccessLevel> = { F: 'full', O: 'own', V: 'view', N: 'none' }

export type PermissionRow = { capability: string; access: readonly Access[] }
export type PermissionGroup = { title: string; rows: readonly PermissionRow[] }

/** "FONNN" → one cell per role column; overrides replace the text (and level) of single cells. */
function row(capability: string, spec: string, overrides: Record<number, Access> = {}): PermissionRow {
  const access = spec.split('').map((c, i) => {
    const level = codes[c]
    return overrides[i] ?? { level, text: defaultText[level] }
  })
  return { capability, access }
}

const scan: Access = { level: 'full', text: 'Scan' }

export const permissionGroups: readonly PermissionGroup[] = [
  {
    title: 'Organization',
    rows: [row('People, levels & promotions', 'FNNNN'), row('Board & goon positions', 'FVVVN')],
  },
  {
    title: 'Events',
    rows: [row('Create & edit events, tickets, vouchers', 'FNNNN'), row('Committees: add members', 'FONNN')],
  },
  {
    title: 'Task board',
    rows: [
      row('Create, assign & score tasks', 'FONNN'),
      row('Move cards, comment, checklists', 'FOOON'),
      row('View the event board', 'FVVVN'),
    ],
  },
  {
    title: 'Registrations & money',
    rows: [
      row('Registrations, manual registration', 'FNNNN'),
      row('Mark as paid, refunds', 'FNNNN'),
      row('Check-in scanner', 'FVVVN', { 1: scan, 2: scan, 3: scan }),
      row('Sponsors & tiers', 'FNNNN'),
    ],
  },
  {
    title: 'Points',
    rows: [row('Leaderboard', 'FVVVN'), row('Multipliers & email branding', 'FNNNN')],
  },
  {
    title: 'Finance (board only)',
    rows: [
      row('View cashflow, ledger, budgets', 'VNNNN'),
      row('Post money in / out, budgets, close month', 'NNNNN', {
        0: { level: 'full', text: 'CFO only' },
        2: { level: 'own', text: '1 assistant, CFO approves' },
      }),
      row('File expense requests', 'FNNNN', { 0: { level: 'full', text: 'Request' } }),
    ],
  },
  {
    title: 'Merch store',
    rows: [
      row('Create & run pop-up stores', 'FONNN', { 1: { level: 'own', text: 'If committee allowed' } }),
      row('Sell at a pop-up (POS)', 'FOOON', {
        2: { level: 'own', text: 'If on store team' },
        3: { level: 'own', text: 'If on store team' },
      }),
    ],
  },
]

export const accessLegend: ReadonlyArray<{ level: AccessLevel; label: string; text: string }> = [
  { level: 'full', label: 'Full', text: 'can create, edit, delete' },
  { level: 'own', label: 'Own', text: 'only their committee or their own tasks' },
  { level: 'view', label: 'View', text: 'read only' },
  { level: 'none', label: 'None', text: 'hidden' },
]
