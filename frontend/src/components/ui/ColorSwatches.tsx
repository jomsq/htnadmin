import { cn } from '@/lib/cn'
import { CheckIcon } from './icons'
import { brandSwatches, type Swatch } from './swatches'

/** Round color presets as a radiogroup (44px targets). The selected one shows a check. */
export function ColorSwatches({
  value,
  onChange,
  swatches = brandSwatches,
  'aria-label': label,
}: {
  value: string
  onChange: (hex: string) => void
  swatches?: ReadonlyArray<Swatch>
  'aria-label': string
}) {
  const current = value.trim().toUpperCase()
  return (
    <div role="radiogroup" aria-label={label} className="flex flex-wrap gap-2">
      {swatches.map((s) => {
        const selected = current === s.hex
        return (
          <button
            key={s.hex}
            type="button"
            role="radio"
            aria-checked={selected}
            aria-label={`${s.name} ${s.hex}`}
            title={`${s.name} ${s.hex}`}
            onClick={() => onChange(s.hex)}
            className={cn(
              'inline-flex size-11 items-center justify-center rounded-full text-white ring-offset-2 dark:ring-offset-gray-900',
              selected ? 'ring-2 ring-gray-900 dark:ring-gray-50' : 'ring-1 ring-gray-900/10 dark:ring-white/15',
            )}
            style={{ backgroundColor: s.hex }}
          >
            {selected ? <CheckIcon size={18} /> : null}
          </button>
        )
      })}
    </div>
  )
}
