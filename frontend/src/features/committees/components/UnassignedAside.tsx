import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { cardClass } from '@/components/ui/Card'
import { cn } from '@/lib/cn'
import { LevelBadge } from '@/features/organization/components/badges'
import { personName, type CommitteesPage } from '../schemas'
import type { AssignTarget } from './AssignDialog'

/** "Not yet assigned": people on the event roster with no committee, plus volunteers. */
export function UnassignedAside({
  pool,
  canAssign,
  canAddVolunteer,
  onAssign,
  onAddVolunteer,
}: {
  pool: CommitteesPage['pool']
  canAssign: boolean
  canAddVolunteer: boolean
  onAssign: (target: AssignTarget) => void
  onAddVolunteer: () => void
}) {
  const isEmpty = pool.people.length === 0 && pool.volunteers.length === 0

  return (
    <aside aria-label="Not yet assigned" className={cn(cardClass, 'flex w-full flex-col lg:w-80 lg:shrink-0')}>
      <div className="flex flex-col gap-1 border-b border-gray-200 px-5 py-4 dark:border-white/10">
        <h2 className="text-base font-semibold text-gray-900 dark:text-gray-50">Not yet assigned</h2>
        <p className="text-[0.8125rem] text-gray-600 dark:text-gray-400">
          {canAssign ? 'Use Assign to put someone on a committee.' : 'Goons, apprentices and volunteers with no committee yet.'}
        </p>
      </div>
      {isEmpty ? (
        <p className="px-5 py-4 text-sm text-gray-600 dark:text-gray-400">Everyone on the roster is on a committee.</p>
      ) : (
        <ul className="flex flex-col">
          {pool.people.map((p) => (
            <li key={p.personId} className="flex items-center gap-2.5 border-b border-gray-100 px-5 py-2.5 dark:border-white/10">
              <div className="flex min-w-0 flex-1 flex-col">
                <span className="truncate text-sm font-medium text-gray-900 dark:text-gray-50">{personName(p)}</span>
                <span className="mt-0.5 w-fit">
                  <LevelBadge level={p.level} />
                </span>
              </div>
              {canAssign ? (
                <Button
                  variant="secondary"
                  size="sm"
                  aria-label={`Assign ${personName(p)}`}
                  onClick={() => onAssign({ kind: 'person', id: p.personId, name: personName(p) })}
                >
                  Assign
                </Button>
              ) : null}
            </li>
          ))}
          {pool.volunteers.map((v) => (
            <li key={v.id} className="flex items-center gap-2.5 border-b border-gray-100 px-5 py-2.5 dark:border-white/10">
              <div className="flex min-w-0 flex-1 flex-col">
                <span className="truncate text-sm font-medium text-gray-900 dark:text-gray-50">{v.name}</span>
                <span className="mt-0.5 w-fit">
                  <Badge color="yellow">Volunteer</Badge>
                </span>
              </div>
              {canAssign ? (
                <Button variant="secondary" size="sm" aria-label={`Assign ${v.name}`} onClick={() => onAssign({ kind: 'volunteer', id: v.id, name: v.name })}>
                  Assign
                </Button>
              ) : null}
            </li>
          ))}
        </ul>
      )}
      {canAddVolunteer ? (
        <div className="px-5 py-3">
          <Button variant="secondary" size="sm" block className="h-10 text-[0.8125rem]" onClick={onAddVolunteer}>
            + Add volunteer (no login)
          </Button>
        </div>
      ) : null}
    </aside>
  )
}
