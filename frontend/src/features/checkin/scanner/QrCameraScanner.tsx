import { useEffect, useRef, useState } from 'react'
import { CameraIcon } from '@/components/ui/icons'
import { cn } from '@/lib/cn'
import type { ResultTone } from '../format'
import { loadQrScanner, type CameraScanner } from './loadQrScanner'

type CameraState = 'starting' | 'running' | 'denied' | 'unavailable' | 'failed'

/** Corner frame color follows the last result (white while idle). */
const frameColors: Record<ResultTone, string> = {
  gray: 'border-white',
  green: 'border-green-400',
  yellow: 'border-yellow-400',
  red: 'border-red-400',
}
const lineColors: Record<ResultTone, string> = { gray: 'bg-white', green: 'bg-green-400', yellow: 'bg-yellow-400', red: 'bg-red-400' }

function errorName(e: unknown): string {
  if (e instanceof DOMException || e instanceof Error) return e.name
  return ''
}

function classify(e: unknown): CameraState {
  const name = errorName(e)
  if (name === 'NotAllowedError' || name === 'SecurityError' || name === 'PermissionDeniedError') return 'denied'
  if (name === 'NotFoundError' || name === 'OverconstrainedError' || name === 'NotReadableError') return 'unavailable'
  // qr-scanner rejects with the plain string "Camera not found." when there is no usable camera.
  if (typeof e === 'string' && /not found/i.test(e)) return 'unavailable'
  return 'failed'
}

type Props = {
  onDecode: (payload: string) => void
  /** Caption under the frame, e.g. "Point the camera at the ticket QR code". */
  caption: string
  /** What staff can do instead when the camera can't be used. */
  fallback: string
  tone?: ResultTone
  className?: string
}

/**
 * Camera QR scanner (qr-scanner, loaded with a dynamic import). Shows a clear message when the camera is
 * blocked or missing; the page keeps search and typing as the fallback.
 */
export function QrCameraScanner({ onDecode, caption, fallback, tone = 'gray', className }: Props) {
  const videoRef = useRef<HTMLVideoElement>(null)
  const onDecodeRef = useRef(onDecode)
  const [state, setState] = useState<CameraState>('starting')

  useEffect(() => {
    onDecodeRef.current = onDecode
  }, [onDecode])

  useEffect(() => {
    let cancelled = false
    let scanner: CameraScanner | null = null
    const video = videoRef.current
    async function start() {
      try {
        const mod = await loadQrScanner()
        if (cancelled || !video) return
        if (!(await mod.hasCamera())) {
          if (!cancelled) setState('unavailable')
          return
        }
        if (cancelled) return
        scanner = mod.create(video, (data) => onDecodeRef.current(data))
        await scanner.start()
        if (!cancelled) setState('running')
      } catch (e) {
        if (!cancelled) setState(classify(e))
      }
    }
    void start()
    return () => {
      cancelled = true
      scanner?.destroy()
    }
  }, [])

  const problem =
    state === 'denied'
      ? `Camera access is blocked. Allow the camera for this site in your browser settings. ${fallback}`
      : state === 'unavailable'
        ? `No camera found on this device. ${fallback}`
        : state === 'failed'
          ? `The camera could not start. ${fallback}`
          : null

  return (
    <div className={cn('relative flex items-center justify-center overflow-hidden bg-gray-800', className)}>
      <video
        ref={videoRef}
        muted
        playsInline
        aria-label="Camera preview"
        className={cn('absolute inset-0 size-full object-cover', state !== 'running' && 'invisible')}
      />
      {problem ? (
        <div role="alert" className="relative flex max-w-xs flex-col items-center gap-3 px-6 text-center text-sm text-gray-100">
          <span aria-hidden="true" className="flex size-12 items-center justify-center rounded-full bg-white/10">
            <CameraIcon size={22} />
          </span>
          <p>{problem}</p>
        </div>
      ) : (
        <>
          <div aria-hidden="true" className="relative size-55">
            {(['left-0 top-0 border-l-4 border-t-4 rounded-tl-xl', 'right-0 top-0 border-r-4 border-t-4 rounded-tr-xl', 'left-0 bottom-0 border-l-4 border-b-4 rounded-bl-xl', 'right-0 bottom-0 border-r-4 border-b-4 rounded-br-xl'] as const).map((c) => (
              <span key={c} className={cn('absolute size-10', c, frameColors[tone])} />
            ))}
            <span className={cn('absolute inset-x-4 top-1/2 h-0.5 opacity-70', lineColors[tone])} />
          </div>
          <span className="absolute inset-x-0 bottom-4 px-4 text-center text-[0.8125rem] text-gray-200">
            {state === 'starting' ? 'Starting the camera…' : caption}
          </span>
        </>
      )}
    </div>
  )
}
