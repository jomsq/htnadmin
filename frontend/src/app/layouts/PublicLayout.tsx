import { useLayoutEffect, type ReactNode } from 'react'
import { applyBrand, clearBrand, contrastWithWhite, isHexColor, MIN_CONTRAST } from '@/app/theme/brand'
import { cn } from '@/lib/cn'

/**
 * Mobile-first public pages (registration, proof upload): no app shell and no sign-in.
 * The theme follows the visitor's system setting (light unless the device is in dark mode).
 * `brandColor` comes from the public API; it only applies when it is a readable hex color.
 */
export function PublicLayout({ brandColor, header, children }: { brandColor?: string | null; header?: ReactNode; children: ReactNode }) {
  useLayoutEffect(() => {
    if (!brandColor || !isHexColor(brandColor) || contrastWithWhite(brandColor) < MIN_CONTRAST) return
    applyBrand(brandColor)
    return () => clearBrand()
  }, [brandColor])

  return (
    <div className="flex min-h-screen flex-col items-center bg-gray-50 sm:px-4 sm:py-8 dark:bg-gray-950">
      <main
        className={cn(
          'flex w-full max-w-xl flex-1 flex-col overflow-hidden bg-white sm:flex-none sm:rounded-lg sm:shadow-sm sm:ring-1 sm:ring-gray-900/5',
          'dark:bg-gray-900 dark:sm:ring-white/10',
        )}
      >
        {header}
        {children}
      </main>
      <footer className="w-full max-w-xl px-5 py-6 text-center text-xs text-gray-600 dark:text-gray-400">
        HackTheNorth.PH Inc. · a non-stock, non-profit corporation
      </footer>
    </div>
  )
}

/** Brand-colored band with the organization, the event name and when / where (canvas frame 10). */
export function PublicHeader({ title, subtitle, logoUrl, eyebrow = 'HackTheNorth.PH' }: { title: string; subtitle?: ReactNode; logoUrl?: string | null; eyebrow?: string }) {
  return (
    <header className="flex flex-col gap-2 bg-brand-700 px-5 py-6 text-white">
      <div className="flex items-center gap-2.5">
        {logoUrl ? <img src={logoUrl} alt="" className="size-7 rounded bg-white object-contain p-0.5" /> : null}
        <span className="text-xs font-semibold tracking-wider text-white/90 uppercase">{eyebrow}</span>
      </div>
      <h1 className="text-2xl font-bold tracking-tight">{title}</h1>
      {subtitle ? <p className="text-sm text-white/90">{subtitle}</p> : null}
    </header>
  )
}
