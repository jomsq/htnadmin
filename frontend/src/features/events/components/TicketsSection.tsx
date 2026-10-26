import { useState } from 'react'
import { errorMessage } from '@/app/errors'
import { Button } from '@/components/ui/Button'
import { Card, CardHeader } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { Table, TBody, Td, Th, THead, Tr } from '@/components/ui/Table'
import { useToast } from '@/components/ui/toast-context'
import { formatPeso } from '@/lib/format'
import { useDeactivateTicketMutation } from '../api'
import { formatShortDay } from '../format'
import type { EventDetail, TicketType } from '../schemas'
import { TicketStatusBadge } from './badges'
import { DeactivateDialog } from './DeactivateDialog'
import { TicketDialog } from './TicketDialog'

function salesWindow(t: TicketType): string {
  const from = formatShortDay(t.salesStartsAt)
  const to = formatShortDay(t.salesEndsAt)
  if (from && to) return `${from} – ${to}`
  if (from) return `From ${from}`
  if (to) return `Until ${to}`
  return t.visibility === 'Hidden' ? 'Assigned by admin' : 'While on sale'
}

const n = (v: number) => v.toLocaleString('en-PH')

type Modal = { kind: 'edit'; ticket: TicketType | null } | { kind: 'deactivate'; ticket: TicketType } | null

/** Details › Tickets (canvas frame 05). Ticket types are deactivated, never deleted. */
export function TicketsSection({ event, canManage }: { event: EventDetail; canManage: boolean }) {
  const { toast } = useToast()
  const deactivate = useDeactivateTicketMutation(event.id)
  const [modal, setModal] = useState<Modal>(null)
  const tickets = [...event.tickets].sort((a, b) => Number(b.isActive) - Number(a.isActive) || a.sortOrder - b.sortOrder)
  const add = canManage ? (
    <Button variant="secondary" size="sm" onClick={() => setModal({ kind: 'edit', ticket: null })}>
      + Ticket type
    </Button>
  ) : null

  function onDeactivate(t: TicketType) {
    deactivate.mutate(t.id, {
      onSuccess: () => {
        setModal(null)
        toast({ tone: 'success', title: `${t.name} was deactivated.` })
      },
      onError: (e) => {
        setModal(null)
        toast({ tone: 'error', title: 'Could not deactivate the ticket type.', description: errorMessage(e) })
      },
    })
  }

  return (
    <Card id="tickets" aria-labelledby="tickets-heading" className="scroll-mt-6">
      <CardHeader title={<span id="tickets-heading">Tickets</span>} description="Group tickets register several people in one checkout." action={add} />
      {tickets.length === 0 ? (
        <EmptyState title="No ticket types yet" description="Add at least one ticket type before you publish." action={add} />
      ) : (
        <Table minWidth={720} aria-label="Ticket types">
          <THead>
            <tr>
              <Th>Ticket</Th>
              <Th className="text-right">Price</Th>
              <Th className="text-right">Sold / qty</Th>
              <Th>Group size</Th>
              <Th>Sales window</Th>
              <Th>Status</Th>
              {canManage ? (
                <Th>
                  <span className="sr-only">Actions</span>
                </Th>
              ) : null}
            </tr>
          </THead>
          <TBody>
            {tickets.map((t) => (
              <Tr key={t.id}>
                <Td className="font-medium text-gray-900 dark:text-gray-50">{t.name}</Td>
                <Td numeric>{t.price === 0 ? 'Free' : formatPeso(t.price)}</Td>
                <Td numeric>
                  {n(t.sold)} / {n(t.quantity)}
                  {t.groupSize > 1 ? ' groups' : ''}
                </Td>
                <Td>{t.groupSize > 1 ? `${t.groupSize} people` : '—'}</Td>
                <Td>{salesWindow(t)}</Td>
                <Td>
                  <TicketStatusBadge status={t.isActive ? t.status : 'Inactive'} />
                </Td>
                {canManage ? (
                  <Td className="text-right">
                    {t.isActive ? (
                      <span className="inline-flex gap-1.5">
                        <Button variant="secondary" size="sm" aria-label={`Edit ${t.name}`} onClick={() => setModal({ kind: 'edit', ticket: t })}>
                          Edit
                        </Button>
                        <Button variant="danger" size="sm" aria-label={`Deactivate ${t.name}`} onClick={() => setModal({ kind: 'deactivate', ticket: t })}>
                          Deactivate
                        </Button>
                      </span>
                    ) : null}
                  </Td>
                ) : null}
              </Tr>
            ))}
          </TBody>
        </Table>
      )}

      {modal?.kind === 'edit' ? <TicketDialog eventId={event.id} ticket={modal.ticket} onClose={() => setModal(null)} /> : null}
      {modal?.kind === 'deactivate' ? (
        <DeactivateDialog
          title={`Deactivate ${modal.ticket.name}?`}
          description="It stops selling and leaves the registration page. People who already registered keep their tickets."
          pending={deactivate.isPending}
          onConfirm={() => onDeactivate(modal.ticket)}
          onClose={() => setModal(null)}
        />
      ) : null}
    </Card>
  )
}
