import { Link } from 'react-router'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { PageHeader } from '@/components/ui/PageHeader'
import { Skeleton } from '@/components/ui/Spinner'
import { CheckSquareIcon } from '@/components/ui/icons'
import { useMyTasksQuery } from './api'
import { PlainTaskCard } from './components/TaskCard'

/** Every open task assigned to the signed-in user, grouped by event (sidebar "My tasks"). */
export function MyTasksPage() {
  const query = useMyTasksQuery()

  if (query.isPending) {
    return (
      <div role="status" aria-label="Loading your tasks" className="flex flex-col gap-4">
        <Skeleton className="h-8 w-48" />
        <Skeleton className="h-40 w-full" />
      </div>
    )
  }

  if (query.isError) {
    return (
      <>
        <PageHeader title="My tasks" description="Open tasks assigned to you across events and committees." />
        <Alert
          tone="red"
          live="alert"
          action={
            <Button variant="secondary" size="sm" onClick={() => void query.refetch()}>
              Try again
            </Button>
          }
        >
          Could not load your tasks. {errorMessage(query.error)}
        </Alert>
      </>
    )
  }

  const { groups, openCount } = query.data

  return (
    <>
      <PageHeader
        title="My tasks"
        description={
          openCount === 1 ? '1 open task assigned to you.' : `${openCount} open tasks assigned to you across events and committees.`
        }
      />

      {groups.length === 0 ? (
        <Card>
          <EmptyState
            icon={<CheckSquareIcon size={22} />}
            title="No open tasks"
            description="Nothing is assigned to you right now. Committee leads assign tasks on the event task board."
          />
        </Card>
      ) : (
        <div className="flex flex-col gap-6">
          {groups.map((group) => (
            <section key={group.eventId} aria-labelledby={`group-${group.eventId}`} className="flex flex-col gap-3">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <h2 id={`group-${group.eventId}`} className="text-base font-semibold text-gray-900 dark:text-gray-50">
                  {group.eventName}
                </h2>
                <Link
                  to={`/events/${encodeURIComponent(group.eventId)}/tasks?mine=1`}
                  className="text-[0.8125rem] font-medium text-brand-600 hover:text-brand-700 dark:text-brand-300"
                >
                  Open board
                </Link>
              </div>
              <ul className="grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-3">
                {group.tasks.map((card) => (
                  <PlainTaskCard
                    key={card.id}
                    card={card}
                    status={card.status ?? 'ToDo'}
                    showStatus
                    href={`/events/${encodeURIComponent(group.eventId)}/tasks/${encodeURIComponent(card.id)}`}
                  />
                ))}
              </ul>
            </section>
          ))}
        </div>
      )}
    </>
  )
}
