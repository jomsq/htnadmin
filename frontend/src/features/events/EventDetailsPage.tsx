import { useState } from 'react'
import { useParams } from 'react-router'
import { useCan } from '@/app/auth/auth-context'
import { Policies } from '@/app/auth/policies'
import { Alert } from '@/components/ui/Alert'
import { useEventQuery } from './api'
import { EventInfoSection } from './components/EventInfoSection'
import { RegistrationSection } from './components/RegistrationSection'
import { SetupSteps, type SetupStep } from './components/SetupSteps'
import { TicketsSection } from './components/TicketsSection'
import { VouchersSection } from './components/VouchersSection'

/** /events/:eventId/details (canvas frame 05, without Speaker travel). The event layout handles loading and errors. */
export function EventDetailsPage() {
  const { eventId = '' } = useParams()
  const canManage = useCan(Policies.EventsManage)
  const query = useEventQuery(eventId)
  const [active, setActive] = useState('info')
  const event = query.data
  if (!event) return null

  const steps: SetupStep[] = [
    { id: 'info', label: 'Event info', complete: !!event.name && !!event.venue && event.capacity > 0 },
    { id: 'tickets', label: 'Tickets', complete: event.tickets.some((t) => t.isActive) },
    ...(canManage ? [{ id: 'vouchers', label: 'Vouchers', complete: event.vouchers.some((v) => v.isActive) }] : []),
    { id: 'registration', label: 'Registration & payment', complete: event.manualRegistration || event.publicPage },
  ]

  return (
    <div className="flex flex-col gap-5">
      {!canManage ? (
        <Alert tone="gray">
          View only. Board members manage event details, tickets and vouchers.
        </Alert>
      ) : null}
      <div className="flex flex-wrap items-start gap-6">
        <SetupSteps steps={steps} active={active} onSelect={setActive} />
        <div className="flex min-w-0 flex-[999_1_560px] flex-col gap-5">
          <EventInfoSection event={event} canManage={canManage} />
          <TicketsSection event={event} canManage={canManage} />
          {canManage ? <VouchersSection event={event} /> : null}
          <RegistrationSection event={event} canManage={canManage} />
        </div>
      </div>
    </div>
  )
}
