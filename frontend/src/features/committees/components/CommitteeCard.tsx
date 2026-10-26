import { Link } from 'react-router'
import { errorMessage } from '@/app/errors'
import { Avatar } from '@/components/ui/Avatar'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { ChevronRightIcon, CloseIcon } from '@/components/ui/icons'
import { useToast } from '@/components/ui/toast-context'
import { LevelBadge } from '@/features/organization/components/badges'
import { useDetachVolunteerMutation, useRemoveMemberMutation } from '../api'
import { countLabel, initials, personName, type Committee } from '../schemas'

type Props = {
  eventId: string
  committee: Committee
  /** Board only: rename, deactivate and the lead (the payload's canCreateCommittee flag). */
  canStructure: boolean
  onAddPeople: () => void
  onChangeLead: () => void
  onRename: () => void
  onDeactivate: () => void
}

/** One committee (canvas frame 06): counts, board link, lead row, members and volunteers. */
export function CommitteeCard({ eventId, committee, canStructure, onAddPeople, onChangeLead, onRename, onDeactivate }: Props) {
  const { toast } = useToast()
  const removeMember = useRemoveMemberMutation(eventId)
  const detachVolunteer = useDetachVolunteerMutation(eventId)
  const { canManage } = committee
  const boardHref = `/events/${encodeURIComponent(eventId)}/tasks?committee=${encodeURIComponent(committee.id)}`

  function remove(name: string, run: () => Promise<void>) {
    void run().then(
      () => toast({ tone: 'success', title: `${name} is off ${committee.name}.` }),
      (e: unknown) => toast({ tone: 'error', title: `Could not remove ${name}.`, description: errorMessage(e) }),
    )
  }

  return (
    <Card className="flex flex-col">
      <div className="flex items-start justify-between gap-2 border-b border-gray-200 px-5 py-4 dark:border-white/10">
        <div className="flex min-w-0 flex-col gap-1">
          <h3 className="flex flex-wrap items-center gap-2 text-base font-semibold text-gray-900 dark:text-gray-50">
            {committee.name}
            {committee.isActive ? null : <Badge color="gray">Inactive</Badge>}
          </h3>
          <p className="text-[0.8125rem] text-gray-600 dark:text-gray-400">
            {countLabel(committee.peopleCount, 'person', 'people')} · {countLabel(committee.openTasks, 'open task', 'open tasks')}
          </p>
        </div>
        <Link
          to={boardHref}
          aria-label={`${committee.name} task board`}
          className="inline-flex min-h-10 shrink-0 items-center gap-0.5 text-[0.8125rem] font-medium text-brand-600 hover:text-brand-700 dark:text-brand-300"
        >
          Board
          <ChevronRightIcon size={14} />
        </Link>
      </div>

      <div className="flex items-center gap-2.5 bg-gray-50 px-5 py-3 dark:bg-white/5">
        {committee.lead ? (
          <>
            <Avatar initials={initials(personName(committee.lead))} tone="brand" />
            <div className="flex min-w-0 flex-1 flex-col">
              <span className="text-sm font-medium text-gray-900 dark:text-gray-50">{personName(committee.lead)}</span>
              <span className="text-xs text-gray-600 dark:text-gray-400">Committee lead · can assign &amp; score tasks</span>
            </div>
            {canStructure ? (
              <Button variant="secondary" size="sm" onClick={onChangeLead}>
                Change lead
              </Button>
            ) : null}
          </>
        ) : (
          <>
            <Avatar initials="?" />
            <div className="flex min-w-0 flex-1 flex-col">
              <span className="text-sm font-medium text-gray-900 dark:text-gray-50">No lead yet</span>
              <span className="text-xs text-gray-600 dark:text-gray-400">A goon leads the committee and can assign &amp; score tasks.</span>
            </div>
            {canStructure ? (
              <Button variant="secondary" size="sm" onClick={onChangeLead}>
                Assign lead
              </Button>
            ) : null}
          </>
        )}
      </div>

      {committee.members.length === 0 && committee.volunteers.length === 0 ? (
        <p className="px-5 py-4 text-sm text-gray-600 dark:text-gray-400">No members yet. Add goons, apprentices or volunteers.</p>
      ) : (
        <ul className="flex flex-col px-5 py-2">
          {committee.members.map((m) => (
            <li key={m.personId} className="flex min-h-10 items-center gap-2.5">
              <span className="min-w-0 flex-1 truncate text-sm text-gray-900 dark:text-gray-50">{personName(m)}</span>
              <LevelBadge level={m.level} />
              {canManage ? (
                <Button
                  variant="ghost"
                  size="sm"
                  className="size-8 px-0"
                  aria-label={`Remove ${personName(m)} from ${committee.name}`}
                  onClick={() => remove(personName(m), () => removeMember.mutateAsync({ committeeId: committee.id, personId: m.personId }))}
                >
                  <CloseIcon size={16} />
                </Button>
              ) : null}
            </li>
          ))}
          {committee.volunteers.map((v) => (
            <li key={v.id} className="flex min-h-10 items-center gap-2.5">
              <span className="min-w-0 flex-1 truncate text-sm text-gray-900 dark:text-gray-50">{v.name}</span>
              <Badge color="yellow">Volunteer</Badge>
              {canManage ? (
                <Button
                  variant="ghost"
                  size="sm"
                  className="size-8 px-0"
                  aria-label={`Remove ${v.name} from ${committee.name}`}
                  onClick={() => remove(v.name, () => detachVolunteer.mutateAsync({ committeeId: committee.id, volunteerId: v.id }))}
                >
                  <CloseIcon size={16} />
                </Button>
              ) : null}
            </li>
          ))}
        </ul>
      )}

      {canManage || canStructure ? (
        <div className="mt-auto flex flex-col gap-2 border-t border-gray-100 px-5 py-3 dark:border-white/10">
          {canManage ? (
            <Button variant="dashed" size="sm" block className="h-10 text-[0.8125rem]" onClick={onAddPeople}>
              + Add goon, apprentice or volunteer
            </Button>
          ) : null}
          {canStructure ? (
            <div className="flex items-center justify-between gap-2">
              <Button variant="ghost" size="sm" onClick={onRename}>
                Rename
              </Button>
              <Button variant="danger" size="sm" onClick={onDeactivate}>
                Deactivate
              </Button>
            </div>
          ) : null}
        </div>
      ) : null}
    </Card>
  )
}
