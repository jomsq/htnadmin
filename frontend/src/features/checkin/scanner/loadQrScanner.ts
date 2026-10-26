/** Thin adapter over the `qr-scanner` package, loaded on demand so it stays out of the main bundle. */

export type CameraScanner = { start(): Promise<void>; destroy(): void }

export type ScannerModule = {
  hasCamera(): Promise<boolean>
  create(video: HTMLVideoElement, onDecode: (data: string) => void): CameraScanner
}

export async function loadQrScanner(): Promise<ScannerModule> {
  const { default: QrScanner } = await import('qr-scanner')
  return {
    hasCamera: () => QrScanner.hasCamera(),
    create: (video, onDecode) =>
      new QrScanner(video, (result) => onDecode(result.data), {
        preferredCamera: 'environment',
        maxScansPerSecond: 5,
        returnDetailedScanResult: true,
        // Silence per-frame "No QR code found" errors.
        onDecodeError: () => undefined,
      }),
  }
}
