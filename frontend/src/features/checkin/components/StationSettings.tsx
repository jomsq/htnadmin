import { Card } from '@/components/ui/Card'
import { Checkbox } from '@/components/ui/Input'

type Props = {
  sound: boolean
  onSound: (on: boolean) => void
  fullscreen: boolean
  fullscreenSupported: boolean
  onFullscreen: (on: boolean) => void
}

const row = 'flex min-h-11 items-center gap-2.5 text-[0.8125rem] text-gray-700 dark:text-gray-200'

/** Desk station settings: sound and full-screen kiosk mode. */
export function StationSettings({ sound, onSound, fullscreen, fullscreenSupported, onFullscreen }: Props) {
  return (
    <Card aria-labelledby="station-settings-heading" className="flex flex-col gap-1 px-5 py-4">
      <h2 id="station-settings-heading" className="text-sm font-semibold">
        Station settings
      </h2>
      <label className={row}>
        <Checkbox checked={sound} onChange={(e) => onSound(e.target.checked)} />
        Sound: beep on success, buzz on error
      </label>
      <label className={row}>
        <Checkbox checked={fullscreen} disabled={!fullscreenSupported} onChange={(e) => onFullscreen(e.target.checked)} />
        <span>
          Full-screen kiosk mode
          {fullscreenSupported ? null : <span className="block text-xs text-gray-600 dark:text-gray-400">Not available in this browser.</span>}
        </span>
      </label>
    </Card>
  )
}
