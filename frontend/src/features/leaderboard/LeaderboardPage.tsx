import { useId } from 'react'
import { useSearchParams } from 'react-router'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { Select } from '@/components/ui/Input'
import { PageHeader } from '@/components/ui/PageHeader'
import { SegmentedControl, type SegmentOption } from '@/components/ui/SegmentedControl'
import { Skeleton } from '@/components/ui/Spinner'
import { ChartIcon } from '@/components/ui/icons'
import { useLeaderboardQuery } from './api'
import { Podium } from './components/Podium'
import { RankingsTable } from './components/RankingsTable'
import { LEADERBOARD_SCOPES, SCOPE_LABELS, type LeaderboardScope } from './schemas'

const labelClass = 'flex items-center gap-2 text-[0.8125rem] font-medium text-gray-700 dark:text-gray-200'
const selectWidth = 'block w-40 sm:w-48'

function isScope(value: string | null): value is LeaderboardScope {
  return !!value && (LEADERBOARD_SCOPES as readonly string[]).includes(value)
}

/**
 * Leaderboard (canvas frame 14). Scope, event and committee live in the URL so a view can be
 * shared. `scope=event` needs an event, so a link without one falls back to the quarter.
 */
export function LeaderboardPage() {
  const [search, setSearch] = useSearchParams()
  const committeeSelectId = useId()
  const eventSelectId = useId()

  const rawScope = search.get('scope')
  const eventId = search.get('eventId') ?? undefined
  const committeeId = search.get('committeeId') ?? undefined
  const scope: LeaderboardScope = isScope(rawScope) && (rawScope !== 'event' || !!eventId) ? rawScope : 'quarter'

  const query = useLeaderboardQuery({ scope, eventId: scope === 'event' ? eventId : undefined, committeeId })

  function setParams(changes: Record<string, string | undefined>) {
    const next = new URLSearchParams(search)
    for (const [key, value] of Object.entries(changes)) {
      if (value) next.set(key, value)
      else next.delete(key)
    }
    setSearch(next, { replace: true })
  }

  const data = query.data
  const events = data?.events ?? []
  const committees = data?.committees ?? []
  const pickedEvent = events.find((e) => e.id === eventId)
  // The event segment is labelled with the event itself (canvas frame 14 shows "HTN Con 2026").
  const eventLabel = pickedEvent?.name ?? events[0]?.name ?? SCOPE_LABELS.event

  const options: Array<SegmentOption<LeaderboardScope>> = [
    { value: 'event', label: eventLabel },
    { value: 'quarter', label: SCOPE_LABELS.quarter },
    { value: 'alltime', label: SCOPE_LABELS.alltime },
  ]

  function pickScope(next: LeaderboardScope) {
    if (next === scope) return
    if (next === 'event') {
      const id = eventId ?? events[0]?.id
      if (!id) return
      setParams({ scope: 'event', eventId: id })
      return
    }
    setParams({ scope: next, eventId: undefined })
  }

  const controls = (
    <>
      {/* size lg keeps every control at 44px for touch (design-system). */}
      <SegmentedControl aria-label="Period" size="lg" options={options} value={scope} onChange={pickScope} />
      {scope === 'event' ? (
        <label htmlFor={eventSelectId} className={labelClass}>
          Event
          <span className={selectWidth}>
            <Select id={eventSelectId} controlSize="lg" value={eventId ?? ''} onChange={(e) => setParams({ scope: 'event', eventId: e.target.value })}>
              {events.map((e) => (
                <option key={e.id} value={e.id}>
                  {e.name}
                </option>
              ))}
            </Select>
          </span>
        </label>
      ) : null}
      <label htmlFor={committeeSelectId} className={labelClass}>
        Committee
        <span className={selectWidth}>
          <Select id={committeeSelectId} controlSize="lg" value={committeeId ?? ''} onChange={(e) => setParams({ committeeId: e.target.value || undefined })}>
            <option value="">All committees</option>
            {committees.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </Select>
        </span>
      </label>
    </>
  )

  return (
    <>
      <PageHeader title="Leaderboard" description="Points from completed tasks. Goons and apprentices only." actions={controls} />

      {query.isError ? (
        <Alert
          tone="red"
          live="alert"
          action={
            <Button variant="secondary" size="sm" onClick={() => void query.refetch()}>
              Try again
            </Button>
          }
        >
          Could not load the leaderboard. {errorMessage(query.error)}
        </Alert>
      ) : !data ? (
        <div role="status" aria-label="Loading the leaderboard" className="flex flex-col gap-6">
          <Skeleton className="mx-auto h-44 w-full max-w-[880px]" />
          <Skeleton className="h-72 w-full" />
        </div>
      ) : data.rows.length === 0 ? (
        <Card>
          <EmptyState
            icon={<ChartIcon size={22} />}
            title="No points yet"
            description="Points appear here once tasks are approved into Done. Goons and apprentices earn them as lead, collaborator or assistant."
          />
        </Card>
      ) : (
        <>
          {data.rows.length >= 3 ? <Podium rows={data.rows} /> : null}
          <Card className="overflow-hidden">
            <RankingsTable rows={data.rows} me={data.me} scopeLabel={data.scopeLabel} />
          </Card>
        </>
      )}
    </>
  )
}
