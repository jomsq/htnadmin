/** Brand color math: WCAG contrast and the hover / dark-mode shades derived from the org brand color. */

export const DEFAULT_BRAND = '#B71C1C'
export const MIN_CONTRAST = 4.5
const HEX = /^#[0-9A-Fa-f]{6}$/

export function isHexColor(value: string): boolean {
  return HEX.test(value)
}

type Rgb = [number, number, number]

function toRgb(hex: string): Rgb {
  const n = Number.parseInt(hex.slice(1), 16)
  return [(n >> 16) & 255, (n >> 8) & 255, n & 255]
}

function toHex([r, g, b]: Rgb): string {
  return `#${[r, g, b].map((c) => Math.round(Math.min(255, Math.max(0, c))).toString(16).padStart(2, '0')).join('').toUpperCase()}`
}

function channel(c: number): number {
  const s = c / 255
  return s <= 0.04045 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4
}

export function relativeLuminance(hex: string): number {
  const [r, g, b] = toRgb(hex)
  return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b)
}

/** WCAG 2.x contrast ratio between two #RRGGBB colors (1–21). */
export function contrastRatio(a: string, b: string): number {
  const la = relativeLuminance(a)
  const lb = relativeLuminance(b)
  const [hi, lo] = la > lb ? [la, lb] : [lb, la]
  return (hi + 0.05) / (lo + 0.05)
}

/** Contrast of white text on the brand color (buttons). */
export function contrastWithWhite(hex: string): number {
  return contrastRatio(hex, '#FFFFFF')
}

function mix(hex: string, other: Rgb, amount: number): string {
  const c = toRgb(hex)
  return toHex([0, 1, 2].map((i) => c[i] + (other[i] - c[i]) * amount) as Rgb)
}

export type BrandVars = { brand: string; hover: string; dark: string }

/**
 * Shades for one brand color. The default keeps the canvas values exactly;
 * others are mixed: hover = 22% toward black, dark-mode text = 45% toward white.
 */
export function brandVars(hex: string): BrandVars {
  const brand = isHexColor(hex) ? hex.toUpperCase() : DEFAULT_BRAND
  if (brand === DEFAULT_BRAND) return { brand, hover: '#8E1616', dark: '#F87171' }
  return { brand, hover: mix(brand, [0, 0, 0], 0.22), dark: mix(brand, [255, 255, 255], 0.45) }
}

/** Sets --brand, --brand-hover and --brand-dark on <html>; brand-* utilities follow (see index.css). */
export function applyBrand(hex: string, root: HTMLElement = document.documentElement): void {
  const v = brandVars(hex)
  root.style.setProperty('--brand', v.brand)
  root.style.setProperty('--brand-hover', v.hover)
  root.style.setProperty('--brand-dark', v.dark)
}

export function clearBrand(root: HTMLElement = document.documentElement): void {
  root.style.removeProperty('--brand')
  root.style.removeProperty('--brand-hover')
  root.style.removeProperty('--brand-dark')
}
