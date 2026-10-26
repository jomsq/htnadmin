import { Link } from 'react-router'
import { useAuth } from '@/app/auth/auth-context'
import { Card, CardHeader } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { PageHeader } from '@/components/ui/PageHeader'
import { Stat } from '@/components/ui/Stat'

const linkClass = 'text-sm font-medium text-brand-600 hover:text-brand-700 dark:text-brand-300'

// Values stay "—" until events, registrations and sponsors exist (later phases). No fake numbers.
const kpis = [
  { label: 'Registrations', sub: 'No event yet' },
  { label: 'Awaiting payment', sub: 'Nothing outstanding' },
  { label: 'Paid', sub: 'No tickets yet' },
  { label: 'Sponsorship committed', sub: 'No sponsors yet' },
]

const sections = [
  { title: 'Payments to verify', link: { to: '/events', label: 'View all' }, empty: 'No payments waiting for review.' },
  { title: 'My tasks', link: { to: '/my-tasks', label: 'Open board' }, empty: 'No tasks assigned to you yet.' },
  { title: 'Upcoming events', link: { to: '/events', label: 'All events' }, empty: 'No upcoming events.' },
  { title: 'Leaderboard · this quarter', link: { to: '/leaderboard', label: 'Full board' }, empty: 'No points awarded this quarter yet.' },
]

export function DashboardPage() {
  const { me } = useAuth()
  return (
    <>
      <PageHeader title="Dashboard" description={me ? `What needs your attention today, ${me.firstName}.` : 'What needs your attention today.'} />

      <section aria-label="Key numbers" className="grid grid-cols-[repeat(auto-fit,minmax(200px,1fr))] gap-4">
        {kpis.map((k) => (
          <Stat key={k.label} label={k.label} value="—" sub={k.sub} />
        ))}
      </section>

      <div className="grid grid-cols-1 items-start gap-4 lg:grid-cols-2">
        {sections.map((s) => (
          <Card key={s.title}>
            <CardHeader
              title={s.title}
              action={
                <Link to={s.link.to} className={linkClass}>
                  {s.link.label}
                </Link>
              }
            />
            <EmptyState title={s.empty} className="py-8" />
          </Card>
        ))}
      </div>
    </>
  )
}
