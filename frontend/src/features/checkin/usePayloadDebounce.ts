import { useCallback, useRef } from 'react'

export const SAME_PAYLOAD_WINDOW_MS = 3000

/**
 * Returns `accept(payload)`: false when the same payload was seen less than `windowMs` ago.
 * Every sighting restarts the window, so a QR held up to the camera is sent once, not every few seconds.
 */
export function usePayloadDebounce(windowMs = SAME_PAYLOAD_WINDOW_MS) {
  const last = useRef<{ payload: string; at: number } | null>(null)
  return useCallback(
    (payload: string) => {
      const now = Date.now()
      const prev = last.current
      last.current = { payload, at: now }
      return !(prev && prev.payload === payload && now - prev.at < windowMs)
    },
    [windowMs],
  )
}
