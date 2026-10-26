import { useState } from 'react'
import { useCan } from '@/app/auth/auth-context'
import { Policies } from '@/app/auth/policies'
import { errorMessage } from '@/app/errors'
import { Avatar } from '@/components/ui/Avatar'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Drawer } from '@/components/ui/Drawer'
import { useToast } from '@/components/ui/toast-context'
import { cn } from '@/lib/cn'
import { formatDate, initials } from '@/lib/format'
import { useDeactivatePersonMutation, useResendInviteMutation, useSendPasswordResetMutation } from '../api'
import { fullName } from '../format'
import type { Person } from '../schemas'
import { LevelBadge } from './badges'

function accountText(p: Person): { text: string; tone: 'green' | 'yellow' | 'gray' | 'red' } {
  switch (p.accountStatus) {
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

export function PersonDrawer({ person, onClose }: { person: Person | null; onClose: () => void }) {
  const canManage = useCan(Policies.PeopleManage)
  const { toast } = useToast()
  const resend = useResendInviteMutation()
  const deactivate = useDeactivatePersonMutation()
  const sendReset = useSendPasswordResetMutation()
  const [confirming, setConfirming] = useState(false)

  if (!person) return null
  const name = fullName(person)
  const account = accountText(person)
  const hasLogin = person.accountStatus === 'Active' || person.accountStatus === 'InvitePending'

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

  function onDeactivate(p: Person) {
    deactivate.mutate(p.id, {
      onSuccess: () => {
        setConfirming(false)
        toast({ tone: 'success', title: `${fullName(p)} was deactivated.` })
        onClose()
      },
      onError: (e) => {
        setConfirming(false)
        toast({ tone: 'error', title: 'Could not deactivate this account.', description: errorMessage(e) })
      },
    })
  }

  return (
    <>
      <Drawer open onClose={onClose} label="Person details">
        <div className="flex flex-col gap-5">
          <div className="flex items-center gap-3.5">
            <Avatar initials={initials(person.firstName, person.lastName)} size="lg" tone="brand" />
            <div className="flex min-w-0 flex-col gap-0.5">
              <p className="text-lg font-semibold text-gray-900 dark:text-gray-50">{name}</p>
              <p className="truncate text-[0.8125rem] text-gray-600 dark:text-gray-400">{person.email}</p>
            </div>
          </div>

          <dl className="grid grid-cols-2 gap-3">
            <div className="flex flex-col gap-1 rounded-md p-3 ring-1 ring-gray-200 dark:ring-white/10">
              <dt className="text-xs text-gray-600 dark:text-gray-400">Level</dt>
              <dd>
                <LevelBadge level={person.level} />
              </dd>
            </div>
            <div className="flex flex-col gap-1 rounded-md p-3 ring-1 ring-gray-200 dark:ring-white/10">
              <dt className="text-xs text-gray-600 dark:text-gray-400">Position</dt>
              <dd className="text-sm font-semibold text-gray-900 dark:text-gray-50">{person.position || '—'}</dd>
            </div>
          </dl>

          <div className="flex flex-col gap-1 text-[0.8125rem]">
            <h3 className="font-semibold text-gray-700 dark:text-gray-300">History</h3>
            <p className="text-gray-700 dark:text-gray-300">
              <span className="inline-block min-w-[88px] text-gray-600 dark:text-gray-400">{formatDate(person.createdAt)}</span>
              Added as {person.level}
            </p>
          </div>

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
                <Button variant="danger" size="sm" onClick={() => setConfirming(true)}>
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

      <Dialog
        open={confirming}
        onClose={() => setConfirming(false)}
        role="alertdialog"
        size="sm"
        title={`Deactivate ${name}?`}
        description="They will be signed out and can no longer sign in. Their record and history are kept."
        footer={
          <>
            <Button variant="secondary" onClick={() => setConfirming(false)}>
              Cancel
            </Button>
            <Button
              variant="destructive"
              loading={deactivate.isPending}
              onClick={() => onDeactivate(person)}
            >
              Deactivate account
            </Button>
          </>
        }
      />
    </>
  )
}
