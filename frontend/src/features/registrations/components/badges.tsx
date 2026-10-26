import { Badge, type BadgeColor } from '@/components/ui/Badge'
import { statusLabels } from '../format'
import type { RegistrationStatus } from '../schemas'

const statusColors: Record<RegistrationStatus, BadgeColor> = {
  PendingPayment: 'yellow',
  Paid: 'green',
  CheckedIn: 'blue',
  Cancelled: 'gray',
}

/** Overdue registrations stay Pending, but no longer hold a seat (the server stops counting them toward capacity). */
const OVERDUE_DESCRIPTION = 'Seat released — payment deadline passed'

/** Status badge, plus a red "Overdue" badge when a pending payment is past its deadline. */
export function RegistrationStatusBadges({ status, overdue }: { status: RegistrationStatus; overdue?: boolean }) {
  return (
    <span className="flex flex-wrap gap-1.5">
      <Badge color={statusColors[status]}>{statusLabels[status]}</Badge>
      {overdue && status === 'PendingPayment' ? (
        <>
          <Badge color="red">Overdue</Badge>
          <span className="w-full text-xs text-gray-600 dark:text-gray-400">{OVERDUE_DESCRIPTION}</span>
        </>
      ) : null}
    </span>
  )
}
