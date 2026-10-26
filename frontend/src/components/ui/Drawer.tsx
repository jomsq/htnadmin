import { useId, useRef, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { useModal } from './useModal'
import { CloseIcon } from './icons'

type DrawerProps = {
  open: boolean
  onClose: () => void
  /** Accessible name of the panel, e.g. "Person details". */
  label: string
  children: ReactNode
  footer?: ReactNode
}

/** Right-side panel for record details. Full width on phones. */
export function Drawer({ open, ...rest }: DrawerProps) {
  if (!open) return null
  return createPortal(<DrawerPanel {...rest} />, document.body)
}

function DrawerPanel({ onClose, label, children, footer }: Omit<DrawerProps, 'open'>) {
  const panelRef = useRef<HTMLDivElement>(null)
  const labelId = useId()
  useModal(true, panelRef, onClose)

  return (
    <div className="fixed inset-0 z-40 flex justify-end">
      <div aria-hidden="true" className="absolute inset-0 bg-gray-950/40" onClick={onClose} />
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={labelId}
        tabIndex={-1}
        className="relative flex h-full w-full max-w-md flex-col bg-white shadow-xl ring-1 ring-gray-900/5 dark:bg-gray-900 dark:ring-white/10"
      >
        <div className="flex items-center justify-between gap-4 border-b border-gray-100 px-5 py-3 dark:border-white/10">
          <h2 id={labelId} className="text-sm font-semibold text-gray-700 dark:text-gray-300">
            {label}
          </h2>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close"
            className="-mr-2 inline-flex size-10 items-center justify-center rounded-md text-gray-600 hover:bg-gray-100 dark:text-gray-400 dark:hover:bg-gray-800"
          >
            <CloseIcon />
          </button>
        </div>
        <div className="flex-1 overflow-y-auto p-5">{children}</div>
        {footer ? <div className="flex flex-wrap gap-2 border-t border-gray-100 px-5 py-4 dark:border-white/10">{footer}</div> : null}
      </div>
    </div>
  )
}
