import { waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { afterEach, describe, expect, it } from 'vitest'
import { boardMe, orgSettings, server, signInAs } from '@/test/server'
import { renderWithProviders } from '@/test/render'
import { BrandSync } from './BrandSync'
import { brandVars, clearBrand, contrastWithWhite } from './brand'

afterEach(() => clearBrand())

describe('brand color', () => {
  it('computes WCAG contrast with white', () => {
    expect(contrastWithWhite('#B71C1C')).toBeCloseTo(6.57, 1)
    expect(contrastWithWhite('#FFFFFF')).toBeCloseTo(1, 5)
    expect(contrastWithWhite('#000000')).toBeCloseTo(21, 5)
  })

  it('keeps the canvas shades for the default brand and derives others', () => {
    expect(brandVars('#b71c1c')).toEqual({ brand: '#B71C1C', hover: '#8E1616', dark: '#F87171' })
    const blue = brandVars('#1D4ED8')
    expect(blue.brand).toBe('#1D4ED8')
    expect(contrastWithWhite(blue.hover)).toBeGreaterThan(contrastWithWhite(blue.brand))
  })

  it('sets --brand on <html> from GET /api/settings/org', async () => {
    signInAs(boardMe)
    server.use(http.get('/api/settings/org', () => HttpResponse.json({ ...orgSettings, branding: { ...orgSettings.branding, brandColor: '#1D4ED8' } })))
    renderWithProviders(<BrandSync />)
    const root = document.documentElement
    await waitFor(() => expect(root.style.getPropertyValue('--brand')).toBe('#1D4ED8'))
    expect(root.style.getPropertyValue('--brand-hover')).toBe(brandVars('#1D4ED8').hover)
  })
})
