import { useLayoutEffect } from 'react'
import { useOrgSettingsQuery } from '@/features/settings/api'
import { applyBrand, clearBrand } from './brand'

/**
 * Applies the organization brand color app-wide by setting --brand on <html>
 * from GET /api/settings/org. Render it inside signed-in layouts only.
 */
export function BrandSync() {
  const { data } = useOrgSettingsQuery()
  const color = data?.branding.brandColor
  useLayoutEffect(() => {
    if (!color) return
    applyBrand(color)
    return () => clearBrand()
  }, [color])
  return null
}
