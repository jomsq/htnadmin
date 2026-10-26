import { useState } from 'react'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { FilterChip } from '@/components/ui/FilterChip'
import { SegmentedControl } from '@/components/ui/SegmentedControl'
import { Badge } from '@/components/ui/Badge'
import { LevelBadge } from '@/features/organization/components/badges'
import { useAddMemberMutation, useAttachVolunteerMutation } from '../api'
import { personName, type Committee, type CommitteePerson, type CommitteeVolunteer } from '../schemas'

type Tab = 'people' | 'volunteers'
type LevelFilter = 'all' | 'Goon' | 'Apprentice'

const tabs = [
  { value: 'people', label: 'Goons & apprentices' },
  { value: 'volunteers', label: 'Volunteers' },
] as const satisfies ReadonlyArray<{ value: Tab; label: string }>

const levelFilters = [
  { value: 'all', label: 'All levels' },
  { value: 'Goon', label: 'Goons' },
  { value: 'Apprentice', label: 'Apprentices' },
] as const satisfies ReadonlyArray<{ value: LevelFilter; label: string }>

/** Put someone from the unassigned pool on this committee (people, or volunteers with no login). */
export function AddPeopleDialog({
  eventId,
  committee,
  people,
  volunteers,
  onClose,
}: {
  eventId: string
  committee: Committee
  people: CommitteePerson[]
  volunteers: CommitteeVolunteer[]
  onClose: () => void
}) {
  const [tab, setTab] = useState<Tab>('people')
  const [level, setLevel] = useState<LevelFilter>('all')
  const [done, setDone] = useState<string | null>(null)
  const [failed, setFailed] = useState<string | null>(null)
  const addMember = useAddMemberMutation(eventId)
  const attach = useAttachVolunteerMutation(eventId)
  const pending = addMember.isPending || attach.isPending

  const shown = people.filter((p) => level === 'all' || p.level === level)

  function add(name: string, run: () => Promise<void>) {
    if (pending) return
    setDone(null)
    setFailed(null)
    void run().then(
      () => setDone(`${name} is on ${committee.name}.`),
      (e: unknown) => setFailed(errorMessage(e)),
    )
  }

  return (
    <Dialog
      open
      onClose={onClose}
      title={`Add to ${committee.name}`}
      description="Only people who are not on another committee for this event are listed."
      footer={
        <Button variant="secondary" onClick={onClose}>
          Done
        </Button>
      }
    >
      <div className="flex flex-col gap-4">
        <SegmentedControl options={tabs} value={tab} onChange={setTab} aria-label="Who to add" fill />
        {done ? (
          <Alert tone="green" live="status">
            {done}
          </Alert>
        ) : null}
        {failed ? (
          <Alert tone="red" live="alert">
            {failed}
          </Alert>
        ) : null}

        {tab === 'people' ? (
          <>
            <div className="flex flex-wrap gap-2">
              {levelFilters.map((f) => (
                <FilterChip key={f.value} selected={level === f.value} onClick={() => setLevel(f.value)}>
                  {f.label}
                </FilterChip>
              ))}
            </div>
            {shown.length === 0 ? (
              <p className="py-4 text-sm text-gray-600 dark:text-gray-400">Nobody is free to add. Everyone is already on a committee.</p>
            ) : (
              <ul className="flex flex-col divide-y divide-gray-100 dark:divide-white/10">
                {shown.map((p) => (
                  <li key={p.personId} className="flex min-h-11 items-center gap-2.5 py-1.5">
                    <span className="min-w-0 flex-1 truncate text-sm text-gray-900 dark:text-gray-50">{personName(p)}</span>
                    <LevelBadge level={p.level} />
                    <Button
                      variant="secondary"
                      size="sm"
                      disabled={pending}
                      aria-label={`Add ${personName(p)} to ${committee.name}`}
                      onClick={() => add(personName(p), () => addMember.mutateAsync({ committeeId: committee.id, personId: p.personId }))}
                    >
                      Add
                    </Button>
                  </li>
                ))}
              </ul>
            )}
          </>
        ) : volunteers.length === 0 ? (
          <p className="py-4 text-sm text-gray-600 dark:text-gray-400">
            No volunteer is free to add. Use “+ Add volunteer (no login)” in the Not yet assigned list.
          </p>
        ) : (
          <ul className="flex flex-col divide-y divide-gray-100 dark:divide-white/10">
            {volunteers.map((v) => (
              <li key={v.id} className="flex min-h-11 items-center gap-2.5 py-1.5">
                <span className="min-w-0 flex-1 truncate text-sm text-gray-900 dark:text-gray-50">{v.name}</span>
                <Badge color="yellow">Volunteer</Badge>
                <Button
                  variant="secondary"
                  size="sm"
                  disabled={pending}
                  aria-label={`Add ${v.name} to ${committee.name}`}
                  onClick={() => add(v.name, () => attach.mutateAsync({ committeeId: committee.id, volunteerId: v.id }))}
                >
                  Add
                </Button>
              </li>
            ))}
          </ul>
        )}
      </div>
    </Dialog>
  )
}
