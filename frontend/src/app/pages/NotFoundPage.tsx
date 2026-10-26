import { Link } from 'react-router'
import { Card } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { buttonClass } from '@/components/ui/button-styles'

export function NotFoundPage() {
  return (
    <Card>
      <EmptyState
        title="Page not found"
        description="The link may be old, or the page moved."
        action={
          <Link to="/" className={buttonClass('secondary')}>
            Back to dashboard
          </Link>
        }
      />
    </Card>
  )
}
