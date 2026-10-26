const dateFmt = new Intl.DateTimeFormat('en-PH', { month: 'short', day: 'numeric', year: 'numeric' })
const pesoFmt = new Intl.NumberFormat('en-PH', { style: 'currency', currency: 'PHP', maximumFractionDigits: 0, minimumFractionDigits: 0 })
const pesoCentsFmt = new Intl.NumberFormat('en-PH', { style: 'currency', currency: 'PHP', maximumFractionDigits: 2, minimumFractionDigits: 2 })

export function formatDate(value: string | Date | null | undefined): string {
  if (!value) return ''
  const d = typeof value === 'string' ? new Date(value) : value
  return Number.isNaN(d.getTime()) ? '' : dateFmt.format(d)
}

/** ₱1,500 for whole pesos, ₱1,500.50 when there are centavos (en-PH grouping). */
export function formatPeso(n: number): string {
  return Number.isInteger(n) ? pesoFmt.format(n) : pesoCentsFmt.format(n)
}

export function initials(first: string, last: string): string {
  return `${first.trim().charAt(0)}${last.trim().charAt(0)}`.toUpperCase()
}

/** Only allow same-origin relative paths as post-login redirects. */
export function safeNext(next: string | null | undefined): string {
  if (!next || !next.startsWith('/') || next.startsWith('//') || next.startsWith('/\\')) return '/'
  return next
}
