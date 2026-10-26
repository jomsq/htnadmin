import { useState } from 'react'
import { useCan } from '@/app/auth/auth-context'
import { Policies } from '@/app/auth/policies'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Card, CardHeader } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { PageHeader } from '@/components/ui/PageHeader'
import { Skeleton } from '@/components/ui/Spinner'
import { Table, TBody, Td, Th, THead, Tr } from '@/components/ui/Table'
import { PeopleIcon } from '@/components/ui/icons'
import { useLevelCounts, usePositionsQuery } from './api'
import { CreateAccountDialog } from './components/CreateAccountDialog'
import { ImportCsvDialog } from './components/ImportCsvDialog'
import { FinanceRolesCard } from './components/FinanceRolesCard'
import { OrganizationTabs } from './components/OrganizationTabs'
import { AssignDialog, EditPositionDialog, NewPositionDialog, VacateDialog } from './components/PositionDialogs'
import type { Position, PositionKind } from './schemas'

type Modal =
  | { kind: 'new'; positionKind: PositionKind }
  | { kind: 'assign' | 'vacate' | 'edit'; position: Position }
  | null

const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`

/** Card titles and sub lines follow the canvas (frame 02, Positions tab). */
const cards: ReadonlyArray<{
  kind: PositionKind
  title: string
  addLabel: string
  empty: string
  sub: (seats: number, goons: number | undefined) => string
}> = [
  {
    kind: 'BoardSeat',
    title: 'Board of Trustees',
    addLabel: 'Add a board seat',
    empty: 'No board seats yet.',
    sub: (seats) => `Non-stock corporation · ${plural(seats, 'seat', 'seats')}`,
  },
  {
    kind: 'GoonPosition',
    title: 'Goon positions',
    addLabel: 'Add a goon position',
    empty: 'No goon positions yet.',
    sub: (_seats, goons) => (goons === undefined ? 'Organizer roles' : `Organizer roles · ${plural(goons, 'goon', 'goons')}`),
  },
]

export function PositionsPage() {
  const canManage = useCan(Policies.PeopleManage)
  const query = usePositionsQuery()
  const counts = useLevelCounts()
  const [modal, setModal] = useState<Modal>(null)
  const [creating, setCreating] = useState(false)
  const [importing, setImporting] = useState(false)
  const close = () => setModal(null)
  const positions = query.data ?? []

  return (
    <>
      <PageHeader
        title="Organization"
        description="Board, goons, apprentices and members. Promote people along the path Member → Apprentice → Goon."
        actions={
          canManage ? (
            <>
              <Button variant="secondary" onClick={() => setImporting(true)}>
                Import CSV
              </Button>
              <Button onClick={() => setCreating(true)}>+ Create account</Button>
            </>
          ) : null
        }
      />
      <OrganizationTabs peopleCount={counts.All} />

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
          {cards.map((c) => {
            const list = positions
              .filter((p) => p.kind === c.kind && (p.isActive || canManage))
              .sort((a, b) => Number(b.isActive) - Number(a.isActive) || a.sortOrder - b.sortOrder)
            return (
              <PositionCard
                key={c.kind}
                title={c.title}
                addLabel={c.addLabel}
                empty={c.empty}
                description={c.sub(list.filter((p) => p.isActive).length, counts.Goon)}
                loading={query.isPending}
                positions={list}
                canManage={canManage}
                onAction={setModal}
                onAdd={() => setModal({ kind: 'new', positionKind: c.kind })}
              />
            )
          })}
        </div>
      )}

      {canManage ? <FinanceRolesCard /> : null}

      {canManage ? <CreateAccountDialog open={creating} onClose={() => setCreating(false)} /> : null}
      {canManage && importing ? <ImportCsvDialog onClose={() => setImporting(false)} /> : null}
      {modal?.kind === 'new' ? <NewPositionDialog initialKind={modal.positionKind} onClose={close} /> : null}
      {modal?.kind === 'assign' ? <AssignDialog position={modal.position} positions={positions} onClose={close} /> : null}
      {modal?.kind === 'vacate' ? <VacateDialog position={modal.position} onClose={close} /> : null}
      {modal?.kind === 'edit' ? <EditPositionDialog position={modal.position} onClose={close} /> : null}
    </>
  )
}

type CardProps = {
  title: string
  addLabel: string
  description: string
  empty: string
  loading: boolean
  positions: Position[]
  canManage: boolean
  onAction: (m: Modal) => void
  onAdd: () => void
}

function PositionCard({ title, addLabel, description, empty, loading, positions, canManage, onAction, onAdd }: CardProps) {
  const headingId = `${title.replace(/\W+/g, '-').toLowerCase()}-heading`
  return (
    <Card aria-labelledby={headingId}>
      <CardHeader
        title={<span id={headingId}>{title}</span>}
        description={description}
        action={
          canManage ? (
            <Button variant="secondary" size="sm" aria-label={addLabel} onClick={onAdd}>
              + Position
            </Button>
          ) : null
        }
      />
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
                    <span className="text-gray-700 dark:text-gray-300">
                      {p.holder.firstName} {p.holder.lastName}
                    </span>
                  ) : (
                    <span className="font-medium text-yellow-800 dark:text-yellow-300">Vacant</span>
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
