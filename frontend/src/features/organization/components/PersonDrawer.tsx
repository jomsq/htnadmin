import { useState } from 'react'
import { useCan } from '@/app/auth/auth-context'
import { Policies } from '@/app/auth/policies'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Avatar } from '@/components/ui/Avatar'
import { Button } from '@/components/ui/Button'
import { Drawer } from '@/components/ui/Drawer'
import { Skeleton } from '@/components/ui/Spinner'
import { useToast } from '@/components/ui/toast-context'
import { cn } from '@/lib/cn'
import { formatDate, initials } from '@/lib/format'
import { usePersonQuery, useResendInviteMutation, useSendPasswordResetMutation } from '../api'
import { fullName } from '../format'
import { isDemotion, levelSchema, nextLevel, type HistoryEntry, type Level, type Person } from '../schemas'
import { FinanceRoleBadges } from './badges'
import { DeactivateDialog } from './DeactivateDialog'
import { EditPersonDialog } from './EditPersonDialog'
import { PathProgress } from './PathProgress'
import { PromoteDialog } from './PromoteDialog'

function accountText(p: Person): { text: string; tone: 'green' | 'yellow' | 'gray' | 'red' } {
  switch (p.accountStatus) {
    case null:
      return { text: 'Account details are visible to the board only.', tone: 'gray' }
    case 'Active':
      return {
        text: p.lastSignInAt ? `Active · last sign-in ${formatDate(p.lastSignInAt)}` : 'Active · has not signed in yet',
        tone: 'green',
      }
    case 'InvitePending':
      return { text: 'Invite sent · not accepted yet (the link expires 48 hours after it is sent)', tone: 'yellow' }
    case 'NoLogin':
      return { text: 'No login · roster only', tone: 'gray' }
    case 'Deactivated':
      return { text: 'Deactivated · cannot sign in. History is kept.', tone: 'red' }
  }
}

const toneClass = {
  green: 'text-green-700 dark:text-green-400',
  yellow: 'text-yellow-800 dark:text-yellow-300',
  gray: 'text-gray-600 dark:text-gray-400',
  red: 'text-red-700 dark:text-red-400',
}

const asLevel = (v: string | null): Level | null => {
  const r = levelSchema.safeParse(v)
  return r.success ? r.data : null
}

function historyText(h: HistoryEntry): string {
  switch (h.kind) {
    case 'Created':
      return `Added as ${h.to ?? 'a person'}`
    case 'LevelChanged': {
      const from = asLevel(h.from)
      const to = asLevel(h.to)
      const verb = from && to && isDemotion(from, to) ? 'Moved' : 'Promoted'
      return `${verb} from ${h.from ?? '—'} to ${h.to ?? '—'}`
    }
    case 'PositionChanged':
      return h.to ? `Position set to ${h.to}` : 'Position cleared'
    case 'PositionAssigned':
      return `Assigned ${h.to ?? 'a position'}`
    case 'PositionVacated':
      return `Left ${h.from ?? 'a position'}`
    case 'Deactivated':
      return 'Deactivated'
    default:
      return [h.from, h.to].filter(Boolean).join(' → ') || h.kind
  }
}

type Modal = { kind: 'promote'; level: Level } | { kind: 'edit' } | { kind: 'deactivate' } | null

export function PersonDrawer({ person: listed, onClose }: { person: Person | null; onClose: () => void }) {
  const canManage = useCan(Policies.PeopleManage)
  const { toast } = useToast()
  const detail = usePersonQuery(listed?.id)
  const resend = useResendInviteMutation()
  const sendReset = useSendPasswordResetMutation()
  const [modal, setModal] = useState<Modal>(null)

  if (!listed) return null
  // Prefer the fresh record (it changes after a promotion or edit).
  const person = detail.data?.person ?? listed
  const name = fullName(person)
  const account = accountText(person)
  const hasLogin = person.accountStatus === 'Active' || person.accountStatus === 'InvitePending'
  const next = nextLevel(person.level)
  const positions = detail.data?.positions ?? []

  function onResend(p: Person) {
    resend.mutate(p.id, {
      onSuccess: () => toast({ tone: 'success', title: `Invite re-sent to ${p.email}.`, description: 'The new link expires in 48 hours.' }),
      onError: (e) => toast({ tone: 'error', title: 'Could not resend the invite.', description: errorMessage(e) }),
    })
  }

  function onSendReset(p: Person) {
    sendReset.mutate(p.id, {
      onSuccess: () =>
        toast({ tone: 'success', title: `Password reset link sent to ${p.email}.`, description: 'The link expires in 30 minutes.' }),
      onError: (e) => toast({ tone: 'error', title: 'Could not send a password reset link.', description: errorMessage(e) }),
    })
  }

  const footer =
    canManage && person.isActive ? (
      <>
        {next ? <Button onClick={() => setModal({ kind: 'promote', level: next })}>Promote to {next}</Button> : null}
        <Button variant="secondary" onClick={() => setModal({ kind: 'promote', level: next ?? (person.level === 'Board' ? 'Goon' : 'Board') })}>
          Change level
        </Button>
        <Button variant="secondary" onClick={() => setModal({ kind: 'edit' })}>
          Edit
        </Button>
      </>
    ) : undefined

  return (
    <>
      <Drawer open onClose={onClose} label="Person details" footer={footer}>
        <div className="flex flex-col gap-5">
          <div className="flex items-center gap-3.5">
            <Avatar initials={initials(person.firstName, person.lastName)} size="lg" tone="brand" />
            <div className="flex min-w-0 flex-col gap-1">
              <p className="text-lg font-semibold text-gray-900 dark:text-gray-50">{name}</p>
              <p className="truncate text-[0.8125rem] text-gray-600 dark:text-gray-400">{person.email}</p>
              {person.isCfo || person.isFinanceAssistant ? (
                <div className="flex flex-wrap gap-1.5">
                  <FinanceRoleBadges isCfo={person.isCfo} isFinanceAssistant={person.isFinanceAssistant} />
                </div>
              ) : null}
            </div>
          </div>

          <dl className="grid grid-cols-2 gap-3">
            <div className="flex flex-col gap-1 rounded-md p-3 ring-1 ring-gray-200 dark:ring-white/10">
              <dt className="text-xs text-gray-600 dark:text-gray-400">Level</dt>
              <dd className="text-sm font-semibold text-gray-900 dark:text-gray-50">{person.level}</dd>
            </div>
            <div className="flex flex-col gap-1 rounded-md p-3 ring-1 ring-gray-200 dark:ring-white/10">
              <dt className="text-xs text-gray-600 dark:text-gray-400">Position</dt>
              <dd className="text-sm font-semibold text-gray-900 dark:text-gray-50">
                {positions.length ? positions.map((p) => p.title).join(', ') : person.position || '—'}
              </dd>
            </div>
          </dl>

          <PathProgress level={person.level} />

          <section aria-labelledby="history-heading" className="flex flex-col gap-1.5 text-[0.8125rem]">
            <h3 id="history-heading" className="font-semibold text-gray-700 dark:text-gray-300">
              History
            </h3>
            {detail.isPending ? (
              <div role="status" aria-label="Loading history" className="flex flex-col gap-1.5">
                <Skeleton className="h-3.5 w-56" />
                <Skeleton className="h-3.5 w-44" />
              </div>
            ) : detail.isError ? (
              <Alert
                tone="red"
                live="alert"
                action={
                  <Button variant="secondary" size="sm" onClick={() => void detail.refetch()}>
                    Try again
                  </Button>
                }
              >
                Could not load history. {errorMessage(detail.error)}
              </Alert>
            ) : detail.data.history.length === 0 ? (
              <p className="text-gray-600 dark:text-gray-400">{canManage ? 'No changes recorded yet.' : 'History is visible to the board only.'}</p>
            ) : (
              <ol className="flex flex-col gap-2">
                {detail.data.history.map((h, i) => (
                  <li key={`${h.at}-${i}`} className="flex gap-2 text-gray-700 dark:text-gray-300">
                    <span className="min-w-[88px] shrink-0 text-gray-600 tabular-nums dark:text-gray-400">{formatDate(h.at)}</span>
                    <span className="flex flex-col">
                      <span>
                        {historyText(h)}
                        {h.actorName ? <span className="text-gray-600 dark:text-gray-400"> · by {h.actorName}</span> : null}
                      </span>
                      {h.reason ? <span className="text-gray-600 dark:text-gray-400">Reason: {h.reason}</span> : null}
                    </span>
                  </li>
                ))}
              </ol>
            )}
          </section>

          <section aria-labelledby="account-heading" className="flex flex-col gap-2 border-t border-gray-100 pt-3 dark:border-white/10">
            <h3 id="account-heading" className="text-[0.8125rem] font-semibold text-gray-700 dark:text-gray-300">
              Account
            </h3>
            <p className={cn('text-[0.8125rem] font-medium', toneClass[account.tone])}>{account.text}</p>
            {canManage && hasLogin ? (
              <div className="flex flex-wrap gap-1.5">
                {person.accountStatus === 'InvitePending' ? (
                  <Button variant="secondary" size="sm" loading={resend.isPending} onClick={() => onResend(person)}>
                    Resend invite
                  </Button>
                ) : (
                  <Button variant="secondary" size="sm" loading={sendReset.isPending} onClick={() => onSendReset(person)}>
                    Send password reset link
                  </Button>
                )}
                <Button variant="danger" size="sm" onClick={() => setModal({ kind: 'deactivate' })}>
                  Deactivate
                </Button>
              </div>
            ) : null}
            <p className="text-xs text-gray-600 dark:text-gray-400">
              Board, goons and apprentices get a login. Members and volunteers are roster only.
            </p>
          </section>
        </div>
      </Drawer>

      {modal?.kind === 'promote' ? <PromoteDialog person={person} initialLevel={modal.level} onClose={() => setModal(null)} /> : null}
      {modal?.kind === 'edit' ? <EditPersonDialog person={person} onClose={() => setModal(null)} /> : null}
      {modal?.kind === 'deactivate' ? (
        <DeactivateDialog
          person={person}
          onClose={() => setModal(null)}
          onDone={() => {
            setModal(null)
            onClose()
          }}
        />
      ) : null}
    </>
  )
}
