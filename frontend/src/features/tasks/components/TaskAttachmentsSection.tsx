import { useState } from 'react'
import { errorMessage } from '@/app/errors'
import { FileButton } from '@/components/ui/FileButton'
import { CloseIcon } from '@/components/ui/icons'
import { useToast } from '@/components/ui/toast-context'
import { useRemoveAttachmentMutation, useUploadAttachmentMutation } from '../api'
import { ATTACHMENT_ACCEPT, attachmentError, fileTypeLabel, formatFileSize, type TaskDetail } from '../schemas'

function FileIcon() {
  return (
    <svg aria-hidden="true" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinejoin="round">
      <path d="M14 3H6v18h12V7zM14 3v4h4" />
    </svg>
  )
}

/** Attachments (canvas frame 08): PNG, JPG or PDF up to 5 MB, downloaded through the API. */
export function TaskAttachmentsSection({ task }: { task: TaskDetail }) {
  const { toast } = useToast()
  const upload = useUploadAttachmentMutation(task.id)
  const remove = useRemoveAttachmentMutation(task.id)
  const [error, setError] = useState<string | null>(null)
  const canWork = task.canWork && task.status !== 'Done'

  function onFile(file: File) {
    const invalid = attachmentError(file)
    setError(null)
    if (invalid) {
      setError(invalid)
      return
    }
    upload.mutate(file, {
      onSuccess: () => toast({ tone: 'success', title: `${file.name} attached.` }),
      onError: (e) => setError(errorMessage(e)),
    })
  }

  return (
    <section className="flex flex-col gap-2.5">
      <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-50">Attachments</h3>
      {task.attachments.length === 0 ? <p className="text-[0.8125rem] text-gray-600 dark:text-gray-400">No files attached yet.</p> : null}
      <ul className="flex flex-wrap gap-2">
        {task.attachments.map((file) => (
          <li
            key={file.id}
            className="flex min-h-10 items-center gap-2 rounded-md px-3 text-[0.8125rem] ring-1 ring-gray-200 dark:ring-white/10"
          >
            <span className="text-gray-600 dark:text-gray-400">
              <FileIcon />
            </span>
            <a
              href={file.url}
              className="font-medium text-brand-600 hover:text-brand-700 dark:text-brand-300"
              aria-label={`Download ${file.fileName} (${fileTypeLabel(file.contentType, file.fileName)}, ${formatFileSize(file.sizeBytes)})`}
            >
              {file.fileName}
            </a>
            <span className="text-gray-600 dark:text-gray-400">
              {fileTypeLabel(file.contentType, file.fileName)} · {formatFileSize(file.sizeBytes)}
            </span>
            {canWork ? (
              <button
                type="button"
                aria-label={`Remove ${file.fileName}`}
                disabled={remove.isPending}
                onClick={() =>
                  remove.mutate(
                    { attachmentId: file.id },
                    { onError: (e) => setError(errorMessage(e)) },
                  )
                }
                className="inline-flex size-9 items-center justify-center rounded-md text-gray-600 hover:bg-gray-100 hover:text-red-700 disabled:opacity-40 dark:text-gray-400 dark:hover:bg-white/5"
              >
                <CloseIcon size={16} />
              </button>
            ) : null}
          </li>
        ))}
      </ul>

      {canWork ? (
        <div className="flex flex-col gap-1.5">
          <FileButton accept={ATTACHMENT_ACCEPT} onFile={onFile} loading={upload.isPending}>
            + Attach
          </FileButton>
          <p className="text-xs text-gray-600 dark:text-gray-400">PNG, JPG or PDF, up to 5 MB.</p>
        </div>
      ) : null}

      <p aria-live="polite" className="text-xs font-medium text-red-700 dark:text-red-400">
        {error}
      </p>
    </section>
  )
}
