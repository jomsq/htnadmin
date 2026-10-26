import { TabNav } from '@/components/ui/Tabs'

/** People / Positions tabs under the Organization header. The People tab shows the roster count (canvas: "People · 86"). */
export function OrganizationTabs({ peopleCount }: { peopleCount?: number }) {
  const tabs = [
    { to: '/people', label: peopleCount === undefined ? 'People' : `People · ${peopleCount}`, end: true },
    { to: '/people/positions', label: 'Positions' },
  ]
  return <TabNav tabs={tabs} aria-label="Organization sections" />
}
