import { useState } from 'react'
import { errorMessage } from '@/app/errors'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Card, CardHeader } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { Table, TBody, Td, Th, THead, Tr } from '@/components/ui/Table'
import { useToast } from '@/components/ui/toast-context'
import { formatPeso } from '@/lib/format'
import { useDeactivateVoucherMutation } from '../api'
import { formatShortDay } from '../format'
import type { EventDetail, Voucher } from '../schemas'
import { DeactivateDialog } from './DeactivateDialog'
import { VoucherDialog } from './VoucherDialog'

type Modal = { kind: 'edit'; voucher: Voucher | null } | { kind: 'deactivate'; voucher: Voucher } | null

/** Details › Vouchers (canvas frame 05). Managers only: the API sends no vouchers to other roles. */
export function VouchersSection({ event }: { event: EventDetail }) {
  const { toast } = useToast()
  const deactivate = useDeactivateVoucherMutation(event.id)
  const [modal, setModal] = useState<Modal>(null)
  const ticketName = new Map(event.tickets.map((t) => [t.id, t.name]))
  const vouchers = [...event.vouchers].sort((a, b) => Number(b.isActive) - Number(a.isActive) || a.code.localeCompare(b.code))
  const add = (
    <Button variant="secondary" size="sm" onClick={() => setModal({ kind: 'edit', voucher: null })}>
      + Voucher
    </Button>
  )

  function appliesTo(v: Voucher): string {
    const names = v.ticketTypeIds.map((id) => ticketName.get(id)).filter((x): x is string => !!x)
    const scope = names.length ? names.join(', ') : 'All tickets'
    return v.source === 'SpeakerPlusOne' ? `${scope} · speaker plus-one` : scope
  }

  function onDeactivate(v: Voucher) {
    deactivate.mutate(v.id, {
      onSuccess: () => {
        setModal(null)
        toast({ tone: 'success', title: `${v.code} was deactivated.` })
      },
      onError: (e) => {
        setModal(null)
        toast({ tone: 'error', title: 'Could not deactivate the voucher.', description: errorMessage(e) })
      },
    })
  }

  return (
    <Card id="vouchers" aria-labelledby="vouchers-heading" className="scroll-mt-6">
      <CardHeader title={<span id="vouchers-heading">Vouchers</span>} action={add} />
      {vouchers.length === 0 ? (
        <EmptyState title="No vouchers yet" description="Percent or peso discounts, for some or all tickets, with an optional limit and expiry." action={add} />
      ) : (
        <Table minWidth={720} aria-label="Vouchers">
          <THead>
            <tr>
              <Th>Code</Th>
              <Th>Discount</Th>
              <Th>Applies to</Th>
              <Th className="text-right">Used / limit</Th>
              <Th>Expires</Th>
              <Th>
                <span className="sr-only">Actions</span>
              </Th>
            </tr>
          </THead>
          <TBody>
            {vouchers.map((v) => (
              <Tr key={v.id}>
                <Td>
                  <span className="flex flex-wrap items-center gap-2">
                    <code className="rounded-md bg-gray-100 px-2 py-0.5 font-mono text-[0.8125rem] font-semibold text-gray-900 dark:bg-white/10 dark:text-gray-50">
                      {v.code}
                    </code>
                    {!v.isActive ? <Badge color="gray">Inactive</Badge> : null}
                  </span>
                </Td>
                <Td>{v.kind === 'Percent' ? `${v.value}% off` : `${formatPeso(v.value)} off`}</Td>
                <Td>{appliesTo(v)}</Td>
                <Td numeric>
                  {v.used.toLocaleString('en-PH')} / {v.usageLimit === null ? 'no limit' : v.usageLimit.toLocaleString('en-PH')}
                </Td>
                <Td>{v.expiresAt ? formatShortDay(v.expiresAt) : 'Never'}</Td>
                <Td className="text-right">
                  {v.isActive ? (
                    <span className="inline-flex gap-1.5">
                      {v.source === 'Manual' ? (
                        <Button variant="secondary" size="sm" aria-label={`Edit ${v.code}`} onClick={() => setModal({ kind: 'edit', voucher: v })}>
                          Edit
                        </Button>
                      ) : null}
                      <Button variant="danger" size="sm" aria-label={`Deactivate ${v.code}`} onClick={() => setModal({ kind: 'deactivate', voucher: v })}>
                        Deactivate
                      </Button>
                    </span>
                  ) : null}
                </Td>
              </Tr>
            ))}
          </TBody>
        </Table>
      )}

      {modal?.kind === 'edit' ? <VoucherDialog eventId={event.id} voucher={modal.voucher} tickets={event.tickets} onClose={() => setModal(null)} /> : null}
      {modal?.kind === 'deactivate' ? (
        <DeactivateDialog
          title={`Deactivate ${modal.voucher.code}?`}
          description="The code stops working for new registrations. Registrations that already used it keep their discount."
          pending={deactivate.isPending}
          onConfirm={() => onDeactivate(modal.voucher)}
          onClose={() => setModal(null)}
        />
      ) : null}
    </Card>
  )
}
