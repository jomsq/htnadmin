export type Swatch = { hex: string; name: string }

/** Preset brand / accent colors; every one has at least 4.5:1 contrast with white text. */
export const brandSwatches: ReadonlyArray<Swatch> = [
  { hex: '#B71C1C', name: 'HTN red' },
  { hex: '#BE185D', name: 'Pink' },
  { hex: '#B45309', name: 'Amber' },
  { hex: '#047857', name: 'Green' },
  { hex: '#0F766E', name: 'Teal' },
  { hex: '#1D4ED8', name: 'Blue' },
  { hex: '#374151', name: 'Gray' },
]
