import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'

/** Confirm deactivating a ticket type or voucher. Nothing is deleted; history and past registrations stay. */
export function DeactivateDialog({
  title,
  description,
  pending,
  onConfirm,
  onClose,
}: {
  title: string
  description: string
  pending: boolean
  onConfirm: () => void
  onClose: () => void
}) {
  return (
    <Dialog
      open
      onClose={onClose}
      role="alertdialog"
      size="sm"
      title={title}
      description={description}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button variant="destructive" onClick={onConfirm} loading={pending}>
            Deactivate
          </Button>
        </>
      }
    />
  )
}
