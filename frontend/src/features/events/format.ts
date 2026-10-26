/** Events are scheduled and shown in Asia/Manila (UTC+8 all year, no daylight saving). */
export const EVENT_TIME_ZONE = 'Asia/Manila'
const MANILA_OFFSET_MS = 8 * 60 * 60 * 1000

const localRe = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2})?$/

/** A value from <input type="datetime-local">. */
export function isLocalDateTime(v: string): boolean {
  return localRe.test(v) && !Number.isNaN(new Date(`${v.slice(0, 16)}:00+08:00`).getTime())
}

/** "2026-11-22T08:00" (Manila wall time) → "2026-11-22T08:00:00+08:00". */
export function fromManilaInput(v: string): string {
  return `${v.slice(0, 16)}:00+08:00`
}

/** ISO instant → "2026-11-22T08:00" in Manila wall time, for datetime-local inputs. */
export function toManilaInput(iso: string | null | undefined): string {
  if (!iso) return ''
  const t = new Date(iso).getTime()
  if (Number.isNaN(t)) return ''
  return new Date(t + MANILA_OFFSET_MS).toISOString().slice(0, 16)
}

const dayFmt = new Intl.DateTimeFormat('en-PH', { timeZone: EVENT_TIME_ZONE, month: 'short', day: 'numeric', year: 'numeric' })
const shortDayFmt = new Intl.DateTimeFormat('en-PH', { timeZone: EVENT_TIME_ZONE, month: 'short', day: 'numeric' })
const timeFmt = new Intl.DateTimeFormat('en-PH', { timeZone: EVENT_TIME_ZONE, hour: 'numeric', minute: '2-digit' })

const valid = (iso: string | null | undefined): iso is string => !!iso && !Number.isNaN(new Date(iso).getTime())

/** "Nov 22, 2026" in Manila. */
export function formatEventDay(iso: string | null | undefined): string {
  return valid(iso) ? dayFmt.format(new Date(iso)) : ''
}

/** "Nov 22" in Manila. */
export function formatShortDay(iso: string | null | undefined): string {
  return valid(iso) ? shortDayFmt.format(new Date(iso)) : ''
}

/** "Nov 22, 2026" for a one-day event, "Nov 22, 2026 – Nov 23, 2026" otherwise. */
export function formatEventDates(startsAt: string, endsAt: string): string {
  const a = formatEventDay(startsAt)
  const b = formatEventDay(endsAt)
  return !b || a === b ? a : `${a} – ${b}`
}

/** "Nov 22, 2026 · 8:00 AM" in Manila. */
export function formatEventDateTime(iso: string | null | undefined): string {
  return valid(iso) ? `${dayFmt.format(new Date(iso))} · ${timeFmt.format(new Date(iso))}` : ''
}

/** Cover fallback colors (dark enough for a white status chip). Picked from the event id so it stays stable. */
const coverColors = ['#8E1616', '#0F766E', '#1D4ED8', '#4B5563', '#B45309', '#374151'] as const

export function coverColor(id: string): string {
  let h = 0
  for (let i = 0; i < id.length; i++) h = (h * 31 + id.charCodeAt(i)) >>> 0
  return coverColors[h % coverColors.length]
}

export function percentSold(sold: number, capacity: number): number {
  if (capacity <= 0) return 0
  return Math.min(100, Math.round((sold / capacity) * 100))
}
