import { Link } from 'react-router'
import { Card } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { PageHeader } from '@/components/ui/PageHeader'
import { ToolIcon } from '@/components/ui/icons'
import { buttonClass } from '@/components/ui/button-styles'

/** Placeholder for sidebar sections that ship in later phases. */
export function ComingSoonPage({ title, description }: { title: string; description: string }) {
  return (
    <>
      <PageHeader title={title} description={description} />
      <Card>
        <EmptyState
          icon={<ToolIcon size={22} />}
          title="Coming soon"
          description={`${title} is part of a later release. Nothing to do here yet.`}
          action={
            <Link to="/" className={buttonClass('secondary')}>
              Back to dashboard
            </Link>
          }
        />
      </Card>
    </>
  )
}
