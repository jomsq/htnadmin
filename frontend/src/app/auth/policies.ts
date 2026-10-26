/** Policy names from .claude/skills/htn-domain/permissions.md used by the UI. The server enforces them. */
export const Policies = {
  PeopleView: 'People.View',
  PeopleManage: 'People.Manage',
  SettingsPersonal: 'Settings.Personal',
  SettingsManage: 'Settings.Manage',
  FinanceView: 'Finance.View',
  /** Any signed-in user: published events. Drafts stay manager-only on the server. */
  EventsView: 'Events.View',
  EventsManage: 'Events.Manage',
  /** Board only: manual registration, mark paid, cancel, revert, resend, export. */
  RegistrationsManage: 'Registrations.Manage',
  /** Any signed-in user: phone scanner and check-in desk. The event must be published. */
  CheckInScan: 'CheckIn.Scan',
} as const

export type Policy = (typeof Policies)[keyof typeof Policies] | (string & {})
