import { useState } from 'react'
import { useCan } from '@/app/auth/auth-context'
import { Policies } from '@/app/auth/policies'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Avatar } from '@/components/ui/Avatar'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Card, CardHeader } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { PageHeader } from '@/components/ui/PageHeader'
import { Skeleton } from '@/components/ui/Spinner'
import { Table, TBody, Td, Th, THead, Tr } from '@/components/ui/Table'
import { PeopleIcon } from '@/components/ui/icons'
import { initials } from '@/lib/format'
import { usePositionsQuery } from './api'
import { FinanceRolesCard } from './components/FinanceRolesCard'
import { OrganizationTabs } from './components/OrganizationTabs'
import { AssignDialog, EditPositionDialog, NewPositionDialog, VacateDialog } from './components/PositionDialogs'
import type { Position, PositionKind } from './schemas'

type Modal =
  | { kind: 'new'; positionKind: PositionKind }
  | { kind: 'assign' | 'vacate' | 'edit'; position: Position }
  | null

const cards: ReadonlyArray<{ kind: PositionKind; title: string; description: string; empty: string }> = [
  {
    kind: 'BoardSeat',
    title: 'Board of Trustees',
    description: 'Seats of the non-stock corporation. Board members are admins.',
    empty: 'No board seats yet.',
  },
  {
    kind: 'GoonPosition',
    title: 'Goon positions',
    description: 'Organizer positions held by goons.',
    empty: 'No goon positions yet.',
  },
]

export function PositionsPage() {
  const canManage = useCan(Policies.PeopleManage)
  const query = usePositionsQuery()
  const [modal, setModal] = useState<Modal>(null)
  const close = () => setModal(null)
  const positions = query.data ?? []

  return (
    <>
      <PageHeader
        title="Organization"
        description="Board, goons, apprentices and members. Promote people along the path Member → Apprentice → Goon."
        actions={canManage ? <Button onClick={() => setModal({ kind: 'new', positionKind: 'GoonPosition' })}>+ Position</Button> : null}
      />
      <OrganizationTabs />

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
          Could not load positions. {errorMessage(query.error)}
        </Alert>
      ) : (
        <div className="grid gap-5 xl:grid-cols-2">
          {cards.map((c) => (
            <PositionCard
              key={c.kind}
              {...c}
              loading={query.isPending}
              positions={positions
                .filter((p) => p.kind === c.kind && (p.isActive || canManage))
                .sort((a, b) => Number(b.isActive) - Number(a.isActive) || a.sortOrder - b.sortOrder)}
              canManage={canManage}
              onAction={setModal}
              onAdd={() => setModal({ kind: 'new', positionKind: c.kind })}
            />
          ))}
        </div>
      )}

      {canManage ? <FinanceRolesCard /> : null}

      {modal?.kind === 'new' ? <NewPositionDialog initialKind={modal.positionKind} onClose={close} /> : null}
      {modal?.kind === 'assign' ? <AssignDialog position={modal.position} positions={positions} onClose={close} /> : null}
      {modal?.kind === 'vacate' ? <VacateDialog position={modal.position} onClose={close} /> : null}
      {modal?.kind === 'edit' ? <EditPositionDialog position={modal.position} onClose={close} /> : null}
    </>
  )
}

type CardProps = {
  kind: PositionKind
  title: string
  description: string
  empty: string
  loading: boolean
  positions: Position[]
  canManage: boolean
  onAction: (m: Modal) => void
  onAdd: () => void
}

function PositionCard({ title, description, empty, loading, positions, canManage, onAction, onAdd }: CardProps) {
  const headingId = `${title.replace(/\W+/g, '-').toLowerCase()}-heading`
  return (
    <Card aria-labelledby={headingId}>
      <CardHeader title={<span id={headingId}>{title}</span>} description={description} />
      {loading ? (
        <div role="status" aria-label={`Loading ${title}`} className="flex flex-col gap-3 p-5">
          <Skeleton className="h-5 w-3/4" />
          <Skeleton className="h-5 w-2/3" />
          <Skeleton className="h-5 w-1/2" />
        </div>
      ) : positions.length === 0 ? (
        <EmptyState
          icon={<PeopleIcon size={22} />}
          title={empty}
          action={
            canManage ? (
              <Button variant="secondary" onClick={onAdd}>
                Add a position
              </Button>
            ) : null
          }
        />
      ) : (
        <Table minWidth={460} aria-label={title}>
          <THead>
            <tr>
              <Th>Title</Th>
              <Th>Holder</Th>
              {canManage ? (
                <Th className="text-right">
                  <span className="sr-only">Actions</span>
                </Th>
              ) : null}
            </tr>
          </THead>
          <TBody>
            {positions.map((p) => (
              <Tr key={p.id}>
                <Td className="font-medium text-gray-900 dark:text-gray-50">
                  <span className="flex flex-wrap items-center gap-2">
                    {p.title}
                    {!p.isActive ? <Badge color="gray">Inactive</Badge> : null}
                  </span>
                </Td>
                <Td>
                  {p.holder ? (
                    <span className="flex items-center gap-2.5">
                      <Avatar initials={initials(p.holder.firstName, p.holder.lastName)} />
                      <span className="text-gray-900 dark:text-gray-50">
                        {p.holder.firstName} {p.holder.lastName}
                      </span>
                    </span>
                  ) : (
                    <Badge color="yellow">Vacant</Badge>
                  )}
                </Td>
                {canManage ? (
                  <Td className="text-right">
                    <span className="inline-flex flex-wrap justify-end gap-1.5">
                      {p.isActive ? (
                        p.holder ? (
                          <>
                            <Button variant="secondary" size="sm" aria-label={`Change ${p.title}`} onClick={() => onAction({ kind: 'assign', position: p })}>
                              Change
                            </Button>
                            <Button variant="danger" size="sm" aria-label={`Vacate ${p.title}`} onClick={() => onAction({ kind: 'vacate', position: p })}>
                              Vacate
                            </Button>
                          </>
                        ) : (
                          <Button variant="secondary" size="sm" aria-label={`Assign ${p.title}`} onClick={() => onAction({ kind: 'assign', position: p })}>
                            Assign
                          </Button>
                        )
                      ) : null}
                      <Button variant="ghost" size="sm" aria-label={`Edit ${p.title}`} onClick={() => onAction({ kind: 'edit', position: p })}>
                        Edit
                      </Button>
                    </span>
                  </Td>
                ) : null}
              </Tr>
            ))}
          </TBody>
        </Table>
      )}
    </Card>
  )
}
