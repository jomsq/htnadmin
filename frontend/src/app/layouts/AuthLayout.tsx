import type { ReactNode } from 'react'
import { cardClass } from '@/components/ui/Card'
import { cn } from '@/lib/cn'

/** Centered sign-in card (canvas frame 00). */
export function AuthLayout({
  title,
  subtitle,
  children,
  footnote = true,
}: {
  title: string
  subtitle?: ReactNode
  children: ReactNode
  footnote?: boolean
}) {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-6 bg-gray-50 px-4 py-12 dark:bg-gray-950">
      <div className="flex flex-col items-center gap-3">
        <div
          aria-hidden="true"
          className="flex size-11 items-center justify-center rounded-lg bg-gray-900 text-[0.8125rem] font-bold text-white dark:bg-gray-50 dark:text-gray-900"
        >
          HTN
        </div>
        <h1 className="text-center text-2xl font-semibold tracking-tight text-gray-900 dark:text-gray-50">{title}</h1>
        {subtitle ? <p className="max-w-[360px] text-center text-sm text-gray-600 dark:text-gray-400">{subtitle}</p> : null}
      </div>

      <div className={cn(cardClass, 'flex w-full max-w-[400px] flex-col gap-[18px] p-7')}>{children}</div>

      {footnote ? (
        <p className="max-w-[400px] text-center text-[0.8125rem] text-gray-600 dark:text-gray-400">
          No account? Accounts are created by the board. Ask your committee lead or email{' '}
          <a href="mailto:admin@hackthenorth.ph" className="font-medium text-brand-600 hover:text-brand-700 dark:text-brand-300">
            admin@hackthenorth.ph
          </a>
          .
        </p>
      ) : null}
    </main>
  )
}
