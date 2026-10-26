import { useEffect, useState } from 'react'
import QRCode from 'qrcode'
import { Skeleton } from './Spinner'

/** Renders a QR code for `value` as an image (generated locally, never sent to a third party). */
export function QrCode({ value, label, size = 192 }: { value: string; label: string; size?: number }) {
  const [src, setSrc] = useState<{ value: string; url: string } | null>(null)

  useEffect(() => {
    let cancelled = false
    QRCode.toDataURL(value, { width: size, margin: 1, errorCorrectionLevel: 'M' })
      .then((url) => {
        if (!cancelled) setSrc({ value, url })
      })
      .catch(() => {
        if (!cancelled) setSrc(null)
      })
    return () => {
      cancelled = true
    }
  }, [value, size])

  if (!src || src.value !== value) return <Skeleton className="size-48" />
  return <img src={src.url} alt={label} width={size} height={size} className="rounded-md bg-white p-2 ring-1 ring-gray-200" />
}
