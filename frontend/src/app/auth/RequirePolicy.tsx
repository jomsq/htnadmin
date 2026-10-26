import type { ReactNode } from 'react'
import { Outlet } from 'react-router'
import { Card } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { LockIcon } from '@/components/ui/icons'
import { useCan } from './auth-context'
import type { Policy } from './policies'

/** Shows a no-access state when the user lacks the policy. UI only; the API enforces it too. */
export function RequirePolicy({ policy, children }: { policy: Policy; children?: ReactNode }) {
  const allowed = useCan(policy)
  if (!allowed) {
    return (
      <Card>
        <EmptyState
          icon={<LockIcon size={22} />}
          title="You do not have access to this page"
          description="Ask a board member if you think you should."
        />
      </Card>
    )
  }
  return children ?? <Outlet />
}
