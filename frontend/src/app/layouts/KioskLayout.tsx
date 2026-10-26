import { Outlet } from 'react-router'
import { BrandSync } from '@/app/theme/BrandSync'

/**
 * Full-screen layout without the sidebar for touch and kiosk screens (check-in scanner and desk, later POS).
 * Pages draw their own header with a back link.
 */
export function KioskLayout() {
  return (
    <div className="min-h-dvh bg-gray-50 text-gray-900 dark:bg-gray-950 dark:text-gray-50">
      <BrandSync />
      <Outlet />
    </div>
  )
}
