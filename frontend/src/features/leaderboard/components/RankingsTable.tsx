import { Avatar } from '@/components/ui/Avatar'
import { Badge } from '@/components/ui/Badge'
import { TBody, THead, Table, Td, Th, Tr } from '@/components/ui/Table'
import { cn } from '@/lib/cn'
import { LEVEL_BADGE, formatPoints, formatSplit, type LeaderboardRow } from '../schemas'

/** Own row: tinted plus a written "You", so the highlight never depends on color alone. */
const MINE = 'bg-brand-50 dark:bg-brand-300/5'

function PersonCell({ row }: { row: LeaderboardRow }) {
  return (
    <Th scope="row" className="px-3 py-3 font-medium first:pl-5 compact:py-2">
      <span className="flex items-center gap-2.5">
        <Avatar initials={row.initials} />
        <span className="flex flex-col">
          <span className="flex items-center gap-2 font-medium text-gray-900 dark:text-gray-50">
            {row.name}
            {row.isMe ? <Badge color="brand">You</Badge> : null}
          </span>
          <span>
            <Badge color={LEVEL_BADGE[row.level]}>{row.level}</Badge>
          </span>
        </span>
      </span>
    </Th>
  )
}

function RankingRow({ row, className }: { row: LeaderboardRow; className?: string }) {
  return (
    <Tr className={cn(row.isMe && MINE, className)}>
      <Td className="font-semibold tabular-nums text-gray-700 dark:text-gray-200">{row.rank}</Td>
      <PersonCell row={row} />
      <Td>{row.committees || '—'}</Td>
      <Td numeric>{row.tasksDone}</Td>
      <Td numeric>{formatSplit(row)}</Td>
      <Td numeric className="font-semibold text-gray-900 dark:text-gray-50">
        {formatPoints(row.points)}
      </Td>
    </Tr>
  )
}

/**
 * Rankings table (canvas frame 14). When the caller is outside the visible list, their row is
 * pinned at the bottom behind a divider.
 */
export function RankingsTable({ rows, me, scopeLabel }: { rows: LeaderboardRow[]; me?: LeaderboardRow | null; scopeLabel: string }) {
  const pinned = me && !rows.some((r) => r.personId === me.personId) ? me : null
  return (
    <Table minWidth={760}>
      <caption className="caption-top px-5 py-4 text-left text-base font-semibold text-gray-900 dark:text-gray-50">Rankings · {scopeLabel}</caption>
      <THead>
        <Tr className="border-t-0">
          <Th className="w-16">Rank</Th>
          <Th>Person</Th>
          <Th>Committee</Th>
          <Th className="text-right">Tasks done</Th>
          <Th className="text-right">As lead / collab / assistant</Th>
          <Th className="text-right">Points</Th>
        </Tr>
      </THead>
      <TBody>
        {rows.map((row) => (
          <RankingRow key={row.personId} row={row} />
        ))}
        {pinned ? <RankingRow row={pinned} className="border-t-2 border-gray-300 dark:border-white/20" /> : null}
      </TBody>
    </Table>
  )
}
