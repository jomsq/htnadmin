/**
 * Static copy of .claude/skills/htn-domain/permissions.md (Roles & permissions, canvas frame 03).
 * Read-only in the UI: the server enforces every policy. Keep this in sync when the matrix changes.
 */

export const roleColumns = ['Board (admin)', 'Committee lead', 'Goon', 'Apprentice'] as const
export type RoleColumn = (typeof roleColumns)[number]

/** true = allowed, false = not allowed, string = allowed with a scope (e.g. "own committee"). */
export type Access = boolean | string

export type PermissionRow = {
  policy: string
  description?: string
  access: readonly [Access, Access, Access, Access]
}

export const permissionRows: readonly PermissionRow[] = [
  { policy: 'People.Manage', description: 'create accounts, levels, promotions, positions', access: [true, false, false, false] },
  { policy: 'People.View', access: [true, 'view', 'view', 'view'] },
  { policy: 'Events.Manage', description: 'event, tickets, vouchers, settings', access: [true, false, false, false] },
  { policy: 'Committees.ManageMembers', access: [true, 'own committee', false, false] },
  { policy: 'Tasks.Manage', description: 'create, assign, set points, approve to Done', access: [true, 'own committee', false, false] },
  { policy: 'Tasks.Work', description: 'move own cards, comment, checklist', access: [true, true, 'own tasks', 'own tasks'] },
  { policy: 'Tasks.ViewBoard', access: [true, true, "events they're on", "events they're on"] },
  {
    policy: 'Registrations.Manage',
    description: 'manual registration, mark paid, cancel, resend',
    access: [true, false, false, false],
  },
  { policy: 'CheckIn.Scan', access: [true, true, true, true] },
  { policy: 'Sponsors.Manage, Leads.Manage, Invitations.Manage, Directory.Manage', access: [true, false, false, false] },
  { policy: 'Directory.View', description: 'contacts only', access: [true, true, false, false] },
  { policy: 'Speakers.Manage', description: 'send / revoke links, view travel', access: [true, 'Programs lead', false, false] },
  { policy: 'Suppliers.Manage', description: 'hire, prices, TIN, bank', access: [true, false, false, false] },
  { policy: 'Suppliers.View', description: 'contacts, docs, reviews', access: [true, true, false, false] },
  { policy: 'Suppliers.Rate', access: [true, "own committee's suppliers", false, false] },
  { policy: 'Merch.Manage', description: 'stock, discounts', access: [true, false, false, false] },
  { policy: 'Merch.Sell', description: 'event POS', access: [true, 'own committee', 'if assigned', 'if assigned'] },
  { policy: 'PopUps.Manage', description: 'create / run pop-up', access: [true, 'if committee allowed', false, false] },
  { policy: 'PopUps.Sell', access: [true, 'if committee allowed', 'if on store team', 'if on store team'] },
  { policy: 'Finance.View', description: 'ledger, budgets, cashflow', access: [true, false, false, false] },
  {
    policy: 'Finance.Post',
    description: 'money in/out, transfers, void, reconcile, close month, budgets',
    access: ['CFO only', false, 'finance assistant: add only, stays Pending', false],
  },
  { policy: 'Finance.Approve', access: ['CFO only', false, false, false] },
  { policy: 'Finance.Request', description: 'expense requests', access: [true, false, false, false] },
  {
    policy: 'Settings.Manage',
    description: 'points multipliers, payment accounts, email branding, org theme',
    access: [true, false, false, false],
  },
  { policy: 'Settings.Personal', description: 'own mode / text size / density', access: [true, true, true, true] },
]

/** Identity roles and the data-scoped rights layered on top of them. */
export const roleNotes = {
  identityRoles: ['Board', 'Goon', 'Apprentice'],
  scopedRights: [
    { name: 'CommitteeLead(eventId, committeeId)', text: 'The lead of a committee in an event.' },
    { name: 'CFO', text: 'A flag on one board member.' },
    { name: 'FinanceAssistant', text: 'A flag on at most one goon.' },
    { name: 'PopUpCommittee(committeeId)', text: 'Set by admins.' },
    { name: 'StoreTeam(popUpId)', text: 'The team of one pop-up store.' },
  ],
  noLogin: 'Members and volunteers have no login.',
} as const

export const permissionRules: readonly string[] = [
  'Denials return 403.',
  "Finance endpoints also return 404 to non-board users, so the module's existence isn't revealed.",
  'Prices, TIN and bank fields are removed from responses for non-board users.',
]
