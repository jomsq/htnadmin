/** Policy names from .claude/skills/htn-domain/permissions.md used by the UI. The server enforces them. */
export const Policies = {
  PeopleView: 'People.View',
  PeopleManage: 'People.Manage',
  SettingsPersonal: 'Settings.Personal',
  FinanceView: 'Finance.View',
} as const

export type Policy = (typeof Policies)[keyof typeof Policies] | (string & {})
