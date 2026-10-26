import { useCallback, useRef, useState } from 'react'
import { isApiError } from '@/app/api'
import { errorMessage } from '@/app/errors'
import { useManualCheckInMutation, useScanMutation } from './api'
import type { ScanOutcome } from './format'
import type { ScanResponse } from './schemas'
import { playBeep, playBuzz } from './sound'
import { usePayloadDebounce } from './usePayloadDebounce'

/** Longest text we send as a scan payload (signed tokens are far shorter). */
const MAX_PAYLOAD = 512

/**
 * Shared by the phone scanner and the desk: sends scans and manual check-ins, keeps the latest outcome
 * (an older response never overwrites a newer one), drops identical payloads within 3 s and plays sounds.
 */
export function useScanFlow(eventId: string, station: string, sound = false) {
  const scan = useScanMutation(eventId)
  const manual = useManualCheckInMutation(eventId)
  const accept = usePayloadDebounce()
  const seq = useRef(0)
  const [state, setState] = useState<{ outcome: ScanOutcome; key: number } | null>(null)

  const run = useCallback(
    async (request: Promise<ScanResponse>) => {
      const id = ++seq.current
      try {
        const data = await request
        if (id !== seq.current) return
        setState({ outcome: { kind: 'result', data }, key: id })
        // A lookup waits for staff to confirm: no sound until the real result.
        if (sound && data.result !== 'Lookup') (data.result === 'Welcome' ? playBeep : playBuzz)()
      } catch (e) {
        if (id !== seq.current) return
        setState({
          outcome: isApiError(e, 429) ? { kind: 'slow' } : { kind: 'error', message: errorMessage(e, { 409: 'Check-in is not open for this event.' }) },
          key: id,
        })
        if (sound) playBuzz()
      }
    },
    [sound],
  )

  const { mutateAsync: scanAsync } = scan
  const { mutateAsync: manualAsync } = manual

  /** Returns false when the payload is empty or a repeat inside the debounce window. */
  const submitPayload = useCallback(
    (raw: string) => {
      const payload = raw.trim().slice(0, MAX_PAYLOAD)
      if (!payload || !accept(payload)) return false
      void run(scanAsync({ payload, station }))
      return true
    },
    [accept, run, scanAsync, station],
  )

  const submitManual = useCallback((attendeeId: string) => run(manualAsync({ attendeeId, station })), [manualAsync, run, station])

  // `outcomeKey` changes on every response, so a repeated identical result is announced again.
  return { outcome: state?.outcome ?? null, outcomeKey: state?.key ?? 0, submitPayload, submitManual, manualPending: manual.isPending }
}
