import { Link } from 'react-router'
import { useCan } from '@/app/auth/auth-context'
import { Policies } from '@/app/auth/policies'
import { Card, CardBody, CardHeader } from '@/components/ui/Card'
import { useLeaderboardQuery } from '../api'
import { formatPoints, formatSplit } from '../schemas'

const factClass = 'flex flex-col gap-1'
const factLabel = 'text-[0.8125rem] font-medium text-gray-600 dark:text-gray-400'
const factValue = 'text-2xl font-semibold tracking-tight text-gray-900 tabular-nums dark:text-gray-50'

/**
 * The caller's own quarter points and rank (dashboard, canvas frame 01). Hidden when the API sends
 * no `me` row — board members are not on the leaderboard.
 */
export function MyPointsCard() {
  const canView = useCan(Policies.LeaderboardView)
  const query = useLeaderboardQuery({ scope: 'quarter' }, canView)
  const me = query.data?.me

  if (!canView || !me) return null

  return (
    <Card>
      <CardHeader
        title="Leaderboard · this quarter"
        action={
          <Link to="/leaderboard" className="text-sm font-medium text-brand-600 hover:text-brand-700 dark:text-brand-300">
            Full board
          </Link>
        }
      />
      <CardBody className="flex flex-wrap items-start gap-8">
        <div className={factClass}>
          <span className={factLabel}>Your points</span>
          <span className={factValue}>{formatPoints(me.points)}</span>
        </div>
        <div className={factClass}>
          <span className={factLabel}>Your rank</span>
          <span className={factValue}>#{me.rank}</span>
        </div>
        <div className={factClass}>
          <span className={factLabel}>Tasks done</span>
          <span className={factValue}>{me.tasksDone}</span>
          <span className="text-[0.8125rem] text-gray-600 dark:text-gray-400">{formatSplit(me)} as lead / collab / assistant</span>
        </div>
      </CardBody>
    </Card>
  )
}
