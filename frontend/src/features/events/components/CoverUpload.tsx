import { useState } from 'react'
import { errorMessage } from '@/app/errors'
import { FileButton } from '@/components/ui/FileButton'
import { UploadIcon } from '@/components/ui/icons'
import { useToast } from '@/components/ui/toast-context'
import { useUploadCoverMutation } from '../api'
import { coverColor } from '../format'
import type { EventDetail } from '../schemas'

const COVER_MAX_BYTES = 2 * 1024 * 1024

function checkCover(file: File): string | null {
  const okType = file.type === 'image/png' || file.type === 'image/jpeg' || /\.(png|jpe?g)$/i.test(file.name)
  if (!okType) return 'Upload a PNG or JPG image.'
  if (file.size > COVER_MAX_BYTES) return 'The image is larger than 2 MB.'
  return null
}

/** Cover image tile (dashed box) with Upload / Replace. PNG or JPG up to 2 MB; the server also checks the pixels. */
export function CoverUpload({ event, canManage }: { event: EventDetail; canManage: boolean }) {
  const { toast } = useToast()
  const upload = useUploadCoverMutation(event.id)
  const [error, setError] = useState<string | null>(null)

  function onFile(file: File) {
    const problem = checkCover(file)
    setError(problem)
    if (problem) return
    upload.mutate(file, {
      onSuccess: () => toast({ tone: 'success', title: 'Cover image uploaded.' }),
      onError: (e) => setError(errorMessage(e, { 400: 'Upload a PNG or JPG up to 2 MB and 4096 × 4096 pixels.' })),
    })
  }

  return (
    <div className="flex flex-col gap-2">
      <div className="flex flex-wrap items-center gap-4 rounded-md border border-dashed border-gray-300 p-4 dark:border-white/20">
        <div className="relative h-14 w-24 shrink-0 overflow-hidden rounded-md" style={{ backgroundColor: coverColor(event.id) }}>
          {event.coverUrl ? <img src={event.coverUrl} alt="Current cover image" className="absolute inset-0 size-full object-cover" /> : null}
        </div>
        <div className="flex min-w-0 flex-1 flex-col gap-0.5">
          <span className="text-sm font-medium text-gray-900 dark:text-gray-50">Cover image</span>
          <span className="text-[0.8125rem] text-gray-600 dark:text-gray-400">1600 × 900 PNG or JPG, up to 2 MB, used on the page and emails</span>
        </div>
        {canManage ? (
          <FileButton accept="image/png,image/jpeg,.png,.jpg,.jpeg" onFile={onFile} loading={upload.isPending} size="sm">
            <UploadIcon size={14} />
            {event.coverUrl ? 'Replace' : 'Upload'}
            <span className="sr-only"> cover image</span>
          </FileButton>
        ) : null}
      </div>
      {error ? (
        <p role="alert" className="text-xs font-medium text-red-700 dark:text-red-400">
          {error}
        </p>
      ) : null}
    </div>
  )
}
