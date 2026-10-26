import { useCallback, useEffect, useState } from 'react'

/** Full-screen kiosk mode via the Fullscreen API. `supported` is false where the API is missing or disabled. */
export function useFullscreen() {
  const supported = typeof document.documentElement.requestFullscreen === 'function' && document.fullscreenEnabled !== false
  const [active, setActive] = useState(() => !!document.fullscreenElement)

  useEffect(() => {
    const sync = () => setActive(!!document.fullscreenElement)
    document.addEventListener('fullscreenchange', sync)
    return () => document.removeEventListener('fullscreenchange', sync)
  }, [])

  const toggle = useCallback(async (on: boolean): Promise<boolean> => {
    try {
      if (on && !document.fullscreenElement) await document.documentElement.requestFullscreen()
      if (!on && document.fullscreenElement) await document.exitFullscreen()
      return true
    } catch {
      return false
    }
  }, [])

  return { supported, active, toggle }
}
