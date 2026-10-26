import { Badge, type BadgeColor } from '@/components/ui/Badge'
import type { AccountStatus, Level } from '../schemas'

const levelColor: Record<Level, BadgeColor> = { Board: 'blue', Goon: 'brand', Apprentice: 'teal', Member: 'gray' }

export function LevelBadge({ level }: { level: Level }) {
  return <Badge color={levelColor[level]}>{level}</Badge>
}

const accountStatusLabel: Record<AccountStatus, string> = {
  Active: 'Active',
  InvitePending: 'Invite pending',
  NoLogin: 'No login',
  Deactivated: 'Deactivated',
}

const statusColor: Record<AccountStatus, BadgeColor> = { Active: 'green', InvitePending: 'yellow', NoLogin: 'gray', Deactivated: 'red' }

export function AccountStatusBadge({ status }: { status: AccountStatus | null }) {
  if (!status) return <span className="text-gray-500 dark:text-gray-400">—</span>
  return <Badge color={statusColor[status]}>{accountStatusLabel[status]}</Badge>
}
