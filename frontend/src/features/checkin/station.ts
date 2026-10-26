import { useCallback, useState } from 'react'
import { DEFAULT_STATION, STATION_MAX } from './schemas'

const STATION_KEY = 'htn.checkin.station'
const SOUND_KEY = 'htn.checkin.sound'

/** localStorage can throw (private mode, storage disabled, quota). Every access is guarded. */
function read(key: string): string | null {
  try {
    return window.localStorage.getItem(key)
  } catch {
    return null
  }
}

function write(key: string, value: string): void {
  try {
    window.localStorage.setItem(key, value)
  } catch {
    // Not saved: the value still applies for this page view.
  }
}

export function cleanStation(value: string | null | undefined): string {
  const v = (value ?? '').trim().slice(0, STATION_MAX)
  return v || DEFAULT_STATION
}

export function readStation(): string {
  return cleanStation(read(STATION_KEY))
}

/**
 * The station name for this device (e.g. "Main entrance", "Desk 2").
 * `draft` is what the field shows (may be empty while typing); `station` is the cleaned value sent with scans.
 */
export function useStationName() {
  const [draft, setDraft] = useState(() => read(STATION_KEY) ?? DEFAULT_STATION)
  const update = useCallback((value: string) => {
    const v = value.slice(0, STATION_MAX)
    setDraft(v)
    write(STATION_KEY, v)
  }, [])
  return { draft, station: cleanStation(draft), setStation: update }
}

/** Desk sound setting (beep on success, buzz on error). On by default. */
export function useSoundSetting() {
  const [on, setOn] = useState(() => read(SOUND_KEY) !== 'off')
  const update = useCallback((value: boolean) => {
    setOn(value)
    write(SOUND_KEY, value ? 'on' : 'off')
  }, [])
  return [on, update] as const
}
