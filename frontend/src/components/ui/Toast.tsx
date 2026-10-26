import { useCallback, useMemo, useRef, useState, type ReactNode } from 'react'
import { cn } from '@/lib/cn'
import { ToastContext, type ToastInput } from './toast-context'
import { AlertIcon, CheckIcon, CloseIcon, InfoIcon } from './icons'

type ToastItem = ToastInput & { id: number }

const toneIcon = {
  success: <CheckIcon className="text-green-700 dark:text-green-400" />,
  error: <AlertIcon className="text-red-700 dark:text-red-400" />,
  info: <InfoIcon className="text-blue-700 dark:text-blue-300" />,
}

export function ToastProvider({ children, duration = 5000 }: { children: ReactNode; duration?: number }) {
  const [items, setItems] = useState<ToastItem[]>([])
  const nextId = useRef(1)

  const dismiss = useCallback((id: number) => setItems((all) => all.filter((t) => t.id !== id)), [])

  const toast = useCallback(
    (t: ToastInput) => {
      const id = nextId.current++
      setItems((all) => [...all, { ...t, id }])
      window.setTimeout(() => dismiss(id), duration)
    },
    [dismiss, duration],
  )

  const api = useMemo(() => ({ toast }), [toast])

  return (
    <ToastContext.Provider value={api}>
      {children}
      <div
        aria-live="polite"
        aria-atomic="false"
        className="pointer-events-none fixed inset-x-0 bottom-0 z-[60] flex flex-col items-center gap-2 p-4 sm:items-end"
      >
        {items.map((t) => (
          <div
            key={t.id}
            role={t.tone === 'error' ? 'alert' : 'status'}
            className={cn(
              'pointer-events-auto flex w-full max-w-sm items-start gap-3 rounded-lg bg-white p-4 shadow-lg ring-1 ring-gray-900/5 dark:bg-gray-800 dark:ring-white/10',
            )}
          >
            <span className="mt-0.5 shrink-0">{toneIcon[t.tone ?? 'success']}</span>
            <div className="flex min-w-0 flex-1 flex-col gap-0.5">
              <p className="text-sm font-medium text-gray-900 dark:text-gray-50">{t.title}</p>
              {t.description ? <p className="text-sm text-gray-600 dark:text-gray-300">{t.description}</p> : null}
            </div>
            <button
              type="button"
              onClick={() => dismiss(t.id)}
              aria-label="Dismiss notification"
              className="-m-2 inline-flex size-9 shrink-0 items-center justify-center rounded-md text-gray-600 hover:bg-gray-100 dark:text-gray-400 dark:hover:bg-gray-700"
            >
              <CloseIcon size={16} />
            </button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  )
}
