import type { ReactNode } from 'react'
import { useId } from 'react'
import { useTheme } from '@/app/theme/theme-context'
import type { Density, TextSize } from '@/app/theme/preferences'
import { Card } from '@/components/ui/Card'
import { ModeSwitch } from '@/components/ui/ModeSwitch'
import { PageHeader } from '@/components/ui/PageHeader'
import { SegmentedControl } from '@/components/ui/SegmentedControl'

const sizeOptions = [
  { value: 'default', label: 'Default' },
  { value: 'large', label: 'Large' },
] as const satisfies ReadonlyArray<{ value: TextSize; label: string }>

const densityOptions = [
  { value: 'comfortable', label: 'Comfortable' },
  { value: 'compact', label: 'Compact' },
] as const satisfies ReadonlyArray<{ value: Density; label: string }>

function Row({ labelId, label, children }: { labelId: string; label: string; children: ReactNode }) {
  return (
    <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
      <span id={labelId} className="min-w-24 text-sm font-medium text-gray-900 dark:text-gray-50">
        {label}
      </span>
      {children}
    </div>
  )
}

export function SettingsPage() {
  const { preferences, resolvedTheme, setPreferences } = useTheme()
  const modeId = useId()
  const sizeId = useId()
  const densityId = useId()
  const modeNote =
    preferences.mode === 'system' ? `Follows your device (${resolvedTheme} right now)` : preferences.mode === 'dark' ? 'Dark' : 'Light'

  return (
    <>
      <PageHeader title="Settings" description="Your display preferences. Changes save automatically." />

      <div className="flex flex-wrap items-start gap-6">
        <nav aria-label="Settings sections" className="w-full max-w-60 flex-[1_1_200px]">
          <ul className="flex flex-col gap-1">
            <li>
              <a
                href="#appearance"
                aria-current="true"
                className="flex min-h-10 items-center rounded-md bg-white px-3 text-sm font-semibold text-gray-900 ring-1 ring-gray-200 dark:bg-gray-900 dark:text-gray-50 dark:ring-white/10"
              >
                Appearance
              </a>
            </li>
          </ul>
        </nav>

        <div className="flex min-w-0 flex-[999_1_560px] flex-col gap-5">
          <Card id="appearance" aria-labelledby="appearance-heading" className="flex flex-col gap-6 p-6">
            <div className="flex flex-col gap-1">
              <h2 id="appearance-heading" className="text-base font-semibold text-gray-900 dark:text-gray-50">
                Appearance
              </h2>
              <p className="text-sm text-gray-600 dark:text-gray-400">"Your display" applies only to you.</p>
            </div>
            <div className="flex flex-col gap-5">
              <h3 className="text-[0.8125rem] font-semibold tracking-wide text-gray-700 uppercase dark:text-gray-300">Your display</h3>
              <Row labelId={modeId} label="Mode">
                <ModeSwitch aria-labelledby={modeId} />
                <span className="text-[0.8125rem] text-gray-600 dark:text-gray-400">{modeNote}</span>
              </Row>
              <Row labelId={sizeId} label="Text size">
                <SegmentedControl
                  options={sizeOptions}
                  value={preferences.textSize}
                  onChange={(textSize) => setPreferences({ textSize })}
                  aria-labelledby={sizeId}
                />
              </Row>
              <Row labelId={densityId} label="Density">
                <SegmentedControl
                  options={densityOptions}
                  value={preferences.density}
                  onChange={(density) => setPreferences({ density })}
                  aria-labelledby={densityId}
                />
              </Row>
            </div>
          </Card>
        </div>
      </div>
    </>
  )
}
