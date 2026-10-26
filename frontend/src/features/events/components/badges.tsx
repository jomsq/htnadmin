import { Badge, type BadgeColor } from '@/components/ui/Badge'
import type { EventStatus, TicketStatus } from '../schemas'

type EventBadgeState = EventStatus | 'SoldOut'

const eventStatus: Record<EventBadgeState, { label: string; color: BadgeColor }> = {
  Draft: { label: 'Draft', color: 'gray' },
  OnSale: { label: 'On sale', color: 'green' },
  SoldOut: { label: 'Sold out', color: 'yellow' },
  Closed: { label: 'Closed', color: 'yellow' },
  Ended: { label: 'Ended', color: 'gray' },
}

/** On sale events at capacity read "Sold out" (canvas frame 04). */
export function EventStatusBadge({ status, sold, capacity, solid }: { status: EventStatus; sold?: number; capacity?: number; solid?: boolean }) {
  const soldOut = status === 'OnSale' && sold !== undefined && capacity !== undefined && capacity > 0 && sold >= capacity
  const s = eventStatus[soldOut ? 'SoldOut' : status]
  return (
    <Badge color={s.color} solid={solid}>
      {s.label}
    </Badge>
  )
}

const ticketStatus: Record<TicketStatus, { label: string; color: BadgeColor }> = {
  OnSale: { label: 'On sale', color: 'green' },
  Scheduled: { label: 'Scheduled', color: 'blue' },
  Ended: { label: 'Ended', color: 'gray' },
  SoldOut: { label: 'Sold out', color: 'yellow' },
  Hidden: { label: 'Hidden', color: 'yellow' },
  Inactive: { label: 'Inactive', color: 'gray' },
}

export function TicketStatusBadge({ status }: { status: TicketStatus }) {
  const s = ticketStatus[status]
  return <Badge color={s.color}>{s.label}</Badge>
}
