import { useTheme } from '@/app/theme/theme-context'
import type { Mode } from '@/app/theme/preferences'
import { SegmentedControl, type SegmentOption } from './SegmentedControl'
import { MonitorIcon, MoonIcon, SunIcon } from './icons'

const modeOptions: ReadonlyArray<SegmentOption<Mode>> = [
  { value: 'light', label: 'Light', icon: <SunIcon /> },
  { value: 'system', label: 'System', icon: <MonitorIcon /> },
  { value: 'dark', label: 'Dark', icon: <MoonIcon /> },
]

/** Icon-only Light / System / Dark switch (radiogroup; each option has aria-label + title tooltip). */
export function ModeSwitch({ className, 'aria-labelledby': labelledBy }: { className?: string; 'aria-labelledby'?: string }) {
  const { preferences, setPreferences } = useTheme()
  return (
    <SegmentedControl
      options={modeOptions}
      value={preferences.mode}
      onChange={(mode) => setPreferences({ mode })}
      aria-label={labelledBy ? undefined : 'Color mode'}
      aria-labelledby={labelledBy}
      className={className}
    />
  )
}
