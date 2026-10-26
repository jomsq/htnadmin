import { useState } from 'react'
import { useParams } from 'react-router'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { Skeleton } from '@/components/ui/Spinner'
import { PeopleIcon } from '@/components/ui/icons'
import { useCommitteesQuery } from './api'
import { AddPeopleDialog } from './components/AddPeopleDialog'
import { AssignDialog, type AssignTarget } from './components/AssignDialog'
import { CommitteeCard } from './components/CommitteeCard'
import { CommitteeDialog } from './components/CommitteeDialog'
import { DeactivateCommitteeDialog } from './components/DeactivateCommitteeDialog'
import { LeadDialog } from './components/LeadDialog'
import { UnassignedAside } from './components/UnassignedAside'
import { VolunteerDialog } from './components/VolunteerDialog'
import type { Committee } from './schemas'

type DialogState =
  | { kind: 'committee'; committee: Committee | null }
  | { kind: 'deactivate'; committee: Committee }
  | { kind: 'lead'; committee: Committee }
  | { kind: 'add'; committee: Committee }
  | { kind: 'assign'; target: AssignTarget }
  | { kind: 'volunteer' }
  | null

/** Committees per event (canvas frame 06), with the unassigned pool aside. */
export function CommitteesPage() {
  const { eventId = '' } = useParams()
  const query = useCommitteesQuery(eventId)
  const [dialog, setDialog] = useState<DialogState>(null)

  if (query.isPending) {
    return (
      <div role="status" aria-label="Loading committees" className="flex flex-col gap-4 lg:flex-row">
        <div className="grid flex-1 grid-cols-1 gap-4 sm:grid-cols-2">
          <Skeleton className="h-56" />
          <Skeleton className="h-56" />
        </div>
        <Skeleton className="h-56 w-full lg:w-80" />
      </div>
    )
  }

  if (query.isError) {
    return (
      <Alert
        tone="red"
        live="alert"
        action={
          <Button variant="secondary" size="sm" onClick={() => void query.refetch()}>
            Try again
          </Button>
        }
      >
        Could not load the committees. {errorMessage(query.error)}
      </Alert>
    )
  }

  const data = query.data
  // Writes are allowed per committee: the lead manages their own, the board manages all.
  const manageable = data.committees.filter((c) => c.canManage && c.isActive)
  const current = dialog && 'committee' in dialog && dialog.committee ? data.committees.find((c) => c.id === dialog.committee?.id) : undefined

  return (
    <>
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div className="flex min-w-0 flex-col gap-1">
          <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-50">Committees</h2>
          <p className="text-sm text-gray-600 dark:text-gray-400">
            One goon leads each committee and can assign &amp; score its tasks. Volunteers help without a login.
          </p>
        </div>
        {data.canCreateCommittee ? <Button onClick={() => setDialog({ kind: 'committee', committee: null })}>+ New committee</Button> : null}
      </div>

      {data.committees.length === 0 ? (
        <div className="flex flex-col gap-4 lg:flex-row lg:items-start">
          <Card className="flex-1">
            <EmptyState
              icon={<PeopleIcon size={22} />}
              title="No committees yet"
              description="Committees hold the people and the task board for this event."
              action={
                data.canCreateCommittee ? (
                  <Button onClick={() => setDialog({ kind: 'committee', committee: null })}>+ New committee</Button>
                ) : undefined
              }
            />
          </Card>
          <UnassignedAside
            pool={data.pool}
            canAssign={manageable.length > 0}
            canAddVolunteer={data.canManageVolunteers}
            onAssign={(target) => setDialog({ kind: 'assign', target })}
            onAddVolunteer={() => setDialog({ kind: 'volunteer' })}
          />
        </div>
      ) : (
        <div className="flex flex-col gap-4 lg:flex-row lg:items-start">
          <div className="grid min-w-0 flex-1 grid-cols-1 gap-4 sm:grid-cols-2">
            {data.committees.map((c) => (
              <CommitteeCard
                key={c.id}
                eventId={eventId}
                committee={c}
                canStructure={data.canCreateCommittee}
                onAddPeople={() => setDialog({ kind: 'add', committee: c })}
                onChangeLead={() => setDialog({ kind: 'lead', committee: c })}
                onRename={() => setDialog({ kind: 'committee', committee: c })}
                onDeactivate={() => setDialog({ kind: 'deactivate', committee: c })}
              />
            ))}
          </div>
          <UnassignedAside
            pool={data.pool}
            canAssign={manageable.length > 0}
            canAddVolunteer={data.canManageVolunteers}
            onAssign={(target) => setDialog({ kind: 'assign', target })}
            onAddVolunteer={() => setDialog({ kind: 'volunteer' })}
          />
        </div>
      )}

      {dialog?.kind === 'committee' ? <CommitteeDialog eventId={eventId} committee={dialog.committee} onClose={() => setDialog(null)} /> : null}
      {dialog?.kind === 'deactivate' ? (
        <DeactivateCommitteeDialog eventId={eventId} committee={current ?? dialog.committee} onClose={() => setDialog(null)} />
      ) : null}
      {dialog?.kind === 'lead' ? (
        <LeadDialog eventId={eventId} committee={current ?? dialog.committee} pool={data.pool.people} onClose={() => setDialog(null)} />
      ) : null}
      {dialog?.kind === 'add' ? (
        <AddPeopleDialog
          eventId={eventId}
          committee={current ?? dialog.committee}
          people={data.pool.people}
          volunteers={data.pool.volunteers}
          onClose={() => setDialog(null)}
        />
      ) : null}
      {dialog?.kind === 'assign' ? (
        <AssignDialog eventId={eventId} target={dialog.target} committees={manageable} onClose={() => setDialog(null)} />
      ) : null}
      {dialog?.kind === 'volunteer' ? <VolunteerDialog eventId={eventId} committees={manageable} onClose={() => setDialog(null)} /> : null}
    </>
  )
}
