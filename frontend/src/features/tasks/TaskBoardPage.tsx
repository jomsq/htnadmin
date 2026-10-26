import { Card } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { CheckSquareIcon } from '@/components/ui/icons'

/** Placeholder for the kanban board (canvas frame 07), built in slice 4B. */
export function TaskBoardPage() {
  return (
    <Card>
      <EmptyState
        icon={<CheckSquareIcon size={22} />}
        title="Task board"
        description="Coming in this phase: five columns, points per task and the leaderboard."
      />
    </Card>
  )
}
