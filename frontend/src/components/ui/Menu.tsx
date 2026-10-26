import { useEffect, useId, useRef, useState, type ReactNode } from 'react'
import { cn } from '@/lib/cn'
import { buttonClass, type ButtonSize, type ButtonVariant } from './button-styles'
import { CheckIcon } from './icons'

export type MenuItem = {
  label: ReactNode
  onSelect: () => void
  disabled?: boolean
  /** Shows a check and aria-checked, for "pick one of these" menus like Move to…. */
  selected?: boolean
}

type MenuProps = {
  /** Visible button content. */
  children: ReactNode
  items: MenuItem[]
  /** Accessible name when the button is icon-only or needs more than its text. */
  'aria-label'?: string
  label?: ReactNode
  variant?: ButtonVariant
  size?: ButtonSize
  align?: 'start' | 'end'
  className?: string
}

/**
 * Dropdown menu (button + role="menu"). Arrow keys move between items, Escape closes and
 * returns focus to the button. Items are real buttons, so touch and screen readers work.
 */
export function Menu({ children, items, label, variant = 'secondary', size = 'sm', align = 'end', className, ...aria }: MenuProps) {
  const [open, setOpen] = useState(false)
  const rootRef = useRef<HTMLDivElement>(null)
  const buttonRef = useRef<HTMLButtonElement>(null)
  const menuId = useId()

  useEffect(() => {
    if (!open) return
    function onPointerDown(e: PointerEvent) {
      if (!rootRef.current?.contains(e.target as Node)) setOpen(false)
    }
    document.addEventListener('pointerdown', onPointerDown)
    return () => document.removeEventListener('pointerdown', onPointerDown)
  }, [open])

  function focusItem(index: number) {
    const buttons = rootRef.current?.querySelectorAll<HTMLButtonElement>('[role="menuitem"]:not(:disabled)')
    if (!buttons?.length) return
    const next = (index + buttons.length) % buttons.length
    buttons[next].focus()
  }

  function onMenuKeyDown(e: React.KeyboardEvent<HTMLDivElement>) {
    const buttons = Array.from(rootRef.current?.querySelectorAll<HTMLButtonElement>('[role="menuitem"]:not(:disabled)') ?? [])
    const current = buttons.indexOf(document.activeElement as HTMLButtonElement)
    if (e.key === 'ArrowDown') {
      e.preventDefault()
      focusItem(current + 1)
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      focusItem(current - 1)
    } else if (e.key === 'Escape' || e.key === 'Tab') {
      setOpen(false)
      if (e.key === 'Escape') {
        e.preventDefault()
        buttonRef.current?.focus()
      }
    }
  }

  return (
    <div ref={rootRef} className={cn('relative', className)}>
      <button
        ref={buttonRef}
        type="button"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? menuId : undefined}
        aria-label={aria['aria-label']}
        onClick={() => setOpen((v) => !v)}
        onKeyDown={(e) => {
          if (e.key === 'ArrowDown' && !open) {
            e.preventDefault()
            setOpen(true)
            window.setTimeout(() => focusItem(0), 0)
          }
        }}
        className={buttonClass(variant, size)}
      >
        {children}
      </button>
      {open ? (
        <div
          id={menuId}
          role="menu"
          aria-label={label ? undefined : aria['aria-label']}
          onKeyDown={onMenuKeyDown}
          className={cn(
            'absolute z-30 mt-1 flex min-w-48 flex-col rounded-md bg-white p-1 shadow-lg ring-1 ring-gray-900/10 dark:bg-gray-900 dark:ring-white/10',
            align === 'end' ? 'right-0' : 'left-0',
          )}
        >
          {label ? <p className="px-2.5 py-1.5 text-xs font-semibold text-gray-600 dark:text-gray-400">{label}</p> : null}
          {items.map((item, i) => (
            <button
              key={i}
              type="button"
              role="menuitem"
              aria-checked={item.selected}
              disabled={item.disabled}
              onClick={() => {
                setOpen(false)
                item.onSelect()
              }}
              className="flex min-h-10 items-center gap-2 rounded-md px-2.5 text-left text-sm font-medium text-gray-700 hover:bg-gray-100 disabled:cursor-not-allowed disabled:text-gray-500 dark:text-gray-200 dark:hover:bg-white/5 dark:disabled:text-gray-500"
            >
              <span className="flex size-4 shrink-0 items-center justify-center text-brand-600 dark:text-brand-300">
                {item.selected ? <CheckIcon size={16} /> : null}
              </span>
              {item.label}
            </button>
          ))}
        </div>
      ) : null}
    </div>
  )
}
