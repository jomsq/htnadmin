import type { ComponentProps } from 'react'
import { cn } from '@/lib/cn'

export const controlClass =
  'block w-full rounded-md bg-white px-3 text-sm text-gray-900 shadow-xs ring-1 ring-inset ring-gray-300 placeholder:text-gray-500 ' +
  'focus:outline-2 focus:-outline-offset-1 focus:outline-brand-600 disabled:cursor-not-allowed disabled:bg-gray-50 disabled:text-gray-600 ' +
  'read-only:bg-gray-50 read-only:text-gray-600 aria-[invalid=true]:ring-red-600 ' +
  'dark:bg-gray-900 dark:text-gray-50 dark:ring-white/15 dark:placeholder:text-gray-500 dark:read-only:bg-gray-800 dark:read-only:text-gray-400 dark:aria-[invalid=true]:ring-red-400 dark:focus:outline-brand-300'

/** xl = 48px with 16px text, for the mobile public forms (no zoom on focus in iOS). */
export type ControlSize = 'md' | 'lg' | 'xl'
const heights: Record<ControlSize, string> = { md: 'h-10', lg: 'h-11 text-[0.9375rem]', xl: 'h-12 text-base' }

export type InputProps = ComponentProps<'input'> & { controlSize?: ControlSize }

export function Input({ controlSize = 'md', className, ...rest }: InputProps) {
  return <input className={cn(controlClass, heights[controlSize], className)} {...rest} />
}

export type TextareaProps = ComponentProps<'textarea'>

export function Textarea({ className, rows = 3, ...rest }: TextareaProps) {
  return <textarea rows={rows} className={cn(controlClass, 'py-2', className)} {...rest} />
}

export type SelectProps = ComponentProps<'select'> & { controlSize?: ControlSize }

export function Select({ controlSize = 'md', className, children, ...rest }: SelectProps) {
  return (
    <select className={cn(controlClass, heights[controlSize], 'pr-8', className)} {...rest}>
      {children}
    </select>
  )
}

export function Checkbox({ className, ...rest }: Omit<ComponentProps<'input'>, 'type'>) {
  return (
    <input
      type="checkbox"
      className={cn('size-4 rounded border-gray-300 accent-brand-600 dark:accent-brand-300', className)}
      {...rest}
    />
  )
}
