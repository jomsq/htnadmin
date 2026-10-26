import { Button } from './Button'

/** "Showing 1–25 of 312" with Previous / Next (shown only when there is more than one page). */
export function Pagination({ page, pageSize, total, onPage }: { page: number; pageSize: number; total: number; onPage: (p: number) => void }) {
  const from = total === 0 ? 0 : (page - 1) * pageSize + 1
  const to = Math.min(page * pageSize, total)
  const last = Math.max(1, Math.ceil(total / pageSize))
  return (
    <nav
      aria-label="Pagination"
      className="flex flex-wrap items-center justify-between gap-3 border-t border-gray-100 px-5 py-3 text-[0.8125rem] text-gray-600 dark:border-white/10 dark:text-gray-400"
    >
      <p>
        Showing <span className="font-medium text-gray-900 tabular-nums dark:text-gray-50">{from}</span>–
        <span className="font-medium text-gray-900 tabular-nums dark:text-gray-50">{to}</span> of{' '}
        <span className="font-medium text-gray-900 tabular-nums dark:text-gray-50">{total}</span>
      </p>
      {last > 1 ? (
        <div className="flex gap-2">
          <Button variant="secondary" size="sm" disabled={page <= 1} onClick={() => onPage(page - 1)}>
            Previous
          </Button>
          <Button variant="secondary" size="sm" disabled={page >= last} onClick={() => onPage(page + 1)}>
            Next
          </Button>
        </div>
      ) : null}
    </nav>
  )
}
