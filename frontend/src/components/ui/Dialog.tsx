import { useId, useRef, type ReactNode, type RefObject } from 'react'
import { createPortal } from 'react-dom'
import { cn } from '@/lib/cn'
import { useModal } from './useModal'
import { CloseIcon } from './icons'

type DialogProps = {
  open: boolean
  onClose: () => void
  title: ReactNode
  description?: ReactNode
  children?: ReactNode
  footer?: ReactNode
  size?: 'sm' | 'md' | 'lg'
  role?: 'dialog' | 'alertdialog'
  initialFocusRef?: RefObject<HTMLElement | null>
}

const widths = { sm: 'max-w-md', md: 'max-w-lg', lg: 'max-w-2xl' }

export function Dialog({ open, ...rest }: DialogProps) {
  if (!open) return null
  return createPortal(<DialogPanel {...rest} />, document.body)
}

function DialogPanel({
  onClose,
  title,
  description,
  children,
  footer,
  size = 'md',
  role = 'dialog',
  initialFocusRef,
}: Omit<DialogProps, 'open'>) {
  const panelRef = useRef<HTMLDivElement>(null)
  const titleId = useId()
  const descId = useId()
  useModal(true, panelRef, onClose, initialFocusRef)

  return (
    <div className="fixed inset-0 z-50 flex items-end justify-center p-0 sm:items-center sm:p-4">
      <div aria-hidden="true" className="absolute inset-0 bg-gray-950/50" onClick={onClose} />
      <div
        ref={panelRef}
        role={role}
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={description ? descId : undefined}
        tabIndex={-1}
        className={cn(
          'relative flex max-h-[92vh] w-full flex-col overflow-hidden rounded-t-lg bg-white shadow-xl ring-1 ring-gray-900/5 sm:rounded-lg dark:bg-gray-900 dark:ring-white/10',
          widths[size],
        )}
      >
        <div className="flex items-start justify-between gap-4 px-5 pt-5">
          <div className="flex flex-col gap-1">
            <h2 id={titleId} className="text-base font-semibold text-gray-900 dark:text-gray-50">
              {title}
            </h2>
            {description ? (
              <p id={descId} className="text-[0.8125rem] text-gray-600 dark:text-gray-400">
                {description}
              </p>
            ) : null}
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close"
            className="-mt-1 -mr-2 inline-flex size-10 shrink-0 items-center justify-center rounded-md text-gray-600 hover:bg-gray-100 dark:text-gray-400 dark:hover:bg-gray-800"
          >
            <CloseIcon />
          </button>
        </div>
        <div className="overflow-y-auto px-5 py-4">{children}</div>
        {footer ? (
          <div className="flex flex-wrap justify-end gap-2 border-t border-gray-100 px-5 py-4 dark:border-white/10">{footer}</div>
        ) : null}
      </div>
    </div>
  )
}
