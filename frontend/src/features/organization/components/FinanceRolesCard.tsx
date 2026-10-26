import { useState, type FormEvent } from 'react'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Card, CardBody, CardHeader } from '@/components/ui/Card'
import { Field } from '@/components/ui/Field'
import { Select } from '@/components/ui/Input'
import { Skeleton } from '@/components/ui/Spinner'
import { LockIcon } from '@/components/ui/icons'
import { useToast } from '@/components/ui/toast-context'
import { useFinanceRolesQuery, usePeopleQuery, useUpdateFinanceRolesMutation } from '../api'
import { fullName } from '../format'
import type { FinanceRoles, Person } from '../schemas'

const hasLogin = (p: Person) => p.isActive && (p.accountStatus === 'Active' || p.accountStatus === 'InvitePending')

/** CFO (one board member) and finance assistant (at most one goon). Board only; the API enforces People.Manage. */
export function FinanceRolesCard() {
  const roles = useFinanceRolesQuery(true)
  const board = usePeopleQuery({ level: 'Board', page: 1, pageSize: 100 })
  const goons = usePeopleQuery({ level: 'Goon', page: 1, pageSize: 100 })

  const loading = roles.isPending || board.isPending || goons.isPending
  const error = roles.error ?? board.error ?? goons.error

  return (
    <Card aria-labelledby="finance-roles-heading">
      <CardHeader
        title={
          <span id="finance-roles-heading" className="inline-flex items-center gap-2">
            Finance roles
            <LockIcon size={14} aria-hidden="true" />
            <span className="sr-only">(board only)</span>
          </span>
        }
        description="Only the CFO posts money, approves, reconciles and closes months. The finance assistant can add entries that stay Pending until the CFO approves."
      />
      <CardBody>
        {loading ? (
          <div role="status" aria-label="Loading finance roles" className="grid gap-4 sm:grid-cols-2">
            <Skeleton className="h-16" />
            <Skeleton className="h-16" />
          </div>
        ) : error || !roles.data || !board.data || !goons.data ? (
          <Alert
            tone="red"
            live="alert"
            action={
              <Button
                variant="secondary"
                size="sm"
                onClick={() => {
                  void roles.refetch()
                  void board.refetch()
                  void goons.refetch()
                }}
              >
                Try again
              </Button>
            }
          >
            Could not load finance roles. {errorMessage(error)}
          </Alert>
        ) : (
          <FinanceRolesForm
            key={`${roles.data.cfo?.id ?? ''}-${roles.data.assistant?.id ?? ''}`}
            roles={roles.data}
            board={board.data.items.filter(hasLogin)}
            goons={goons.data.items.filter(hasLogin)}
          />
        )}
      </CardBody>
    </Card>
  )
}

function FinanceRolesForm({ roles, board, goons }: { roles: FinanceRoles; board: Person[]; goons: Person[] }) {
  const { toast } = useToast()
  const mutation = useUpdateFinanceRolesMutation()
  const [cfo, setCfo] = useState(roles.cfo?.id ?? '')
  const [assistant, setAssistant] = useState(roles.assistant?.id ?? '')
  const dirty = cfo !== (roles.cfo?.id ?? '') || assistant !== (roles.assistant?.id ?? '')

  function submit(e: FormEvent) {
    e.preventDefault()
    if (mutation.isPending || !dirty) return
    mutation.mutate(
      { cfoPersonId: cfo || null, assistantPersonId: assistant || null },
      {
        onSuccess: () => toast({ tone: 'success', title: 'Finance roles saved.' }),
        onError: (err) => toast({ tone: 'error', title: 'Could not save finance roles.', description: errorMessage(err) }),
      },
    )
  }

  return (
    <form noValidate onSubmit={submit} className="flex flex-col gap-4">
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="CFO" hint="An active board member with a login.">
          {(p) => (
            <Select {...p} value={cfo} onChange={(e) => setCfo(e.target.value)}>
              <option value="">No CFO</option>
              {board.map((b) => (
                <option key={b.id} value={b.id}>
                  {fullName(b)}
                </option>
              ))}
            </Select>
          )}
        </Field>
        <Field label="Finance assistant" hint="An active goon with a login. At most one.">
          {(p) => (
            <Select {...p} value={assistant} onChange={(e) => setAssistant(e.target.value)}>
              <option value="">No finance assistant</option>
              {goons.map((g) => (
                <option key={g.id} value={g.id}>
                  {fullName(g)}
                </option>
              ))}
            </Select>
          )}
        </Field>
      </div>
      <div>
        <Button type="submit" loading={mutation.isPending} disabled={!dirty}>
          Save finance roles
        </Button>
      </div>
    </form>
  )
}
