import { TabNav } from '@/components/ui/Tabs'

const tabs = [
  { to: '/people', label: 'People', end: true },
  { to: '/people/positions', label: 'Positions' },
] as const

/** People / Positions tabs under the Organization header. */
export function OrganizationTabs() {
  return <TabNav tabs={tabs} aria-label="Organization sections" />
}
