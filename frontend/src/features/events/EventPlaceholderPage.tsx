import { Card } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { ToolIcon } from '@/components/ui/icons'

/** Event tabs that ship later in Phase 3 (Registrations, Emails, Check-in). */
export function EventPlaceholderPage({ title, description }: { title: string; description: string }) {
  return (
    <Card aria-label={title}>
      <EmptyState icon={<ToolIcon size={22} />} title="Coming in this phase" description={description} />
    </Card>
  )
}
