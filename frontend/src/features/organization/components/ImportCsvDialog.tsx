import { useState } from 'react'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { FileButton } from '@/components/ui/FileButton'
import { Table, TBody, Td, Th, THead, Tr } from '@/components/ui/Table'
import { UploadIcon } from '@/components/ui/icons'
import { useToast } from '@/components/ui/toast-context'
import { cn } from '@/lib/cn'
import { useImportPeopleMutation } from '../api'
import { IMPORT_HEADER, IMPORT_MAX_BYTES, type ImportResult } from '../schemas'

function checkFile(file: File): string | null {
  if (!/\.csv$/i.test(file.name)) return 'Choose a .csv file.'
  if (file.size > IMPORT_MAX_BYTES) return 'The file is larger than 1 MB. Split it into smaller files (up to 500 rows each).'
  if (file.size === 0) return 'The file is empty.'
  return null
}

/** Upload a CSV, preview it (dry run) with per-row errors, then confirm the import. */
export function ImportCsvDialog({ onClose }: { onClose: () => void }) {
  const { toast } = useToast()
  const preview = useImportPeopleMutation()
  const commit = useImportPeopleMutation()
  const [file, setFile] = useState<File | null>(null)
  const [fileError, setFileError] = useState<string | null>(null)
  const result: ImportResult | undefined = preview.data

  function choose(f: File) {
    commit.reset()
    preview.reset()
    const problem = checkFile(f)
    setFileError(problem)
    setFile(problem ? null : f)
    if (!problem) preview.mutate({ file: f, dryRun: true })
  }

  function confirm() {
    if (!file || commit.isPending) return
    commit.mutate(
      { file, dryRun: false },
      {
        onSuccess: (r) => {
          toast({
            tone: 'success',
            title: `Imported ${r.created} ${r.created === 1 ? 'person' : 'people'}.`,
            description: 'Apprentices and goons were emailed an invite. Members are roster only.',
          })
          onClose()
        },
      },
    )
  }

  const canConfirm = !!result && result.invalid === 0 && result.valid > 0 && !preview.isPending

  return (
    <Dialog
      open
      onClose={onClose}
      size="lg"
      title="Import people from CSV"
      description="Preview first: nothing is saved until you confirm."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button onClick={confirm} disabled={!canConfirm} loading={commit.isPending}>
            {result && result.valid > 0 ? `Import ${result.valid} ${result.valid === 1 ? 'person' : 'people'}` : 'Import'}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4">
        <div className="flex flex-col gap-2 text-[0.8125rem] text-gray-700 dark:text-gray-300">
          <p>
            Header row: <code className="rounded bg-gray-100 px-1.5 py-0.5 font-mono text-xs dark:bg-white/10">{IMPORT_HEADER}</code>
          </p>
          <p className="text-gray-600 dark:text-gray-400">
            Level is Member, Apprentice or Goon (default Member). Board seats can't be imported. Up to 500 rows, 1 MB.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-3">
          <FileButton accept=".csv,text/csv" onFile={choose} loading={preview.isPending}>
            <UploadIcon size={16} />
            {file ? 'Choose another file' : 'Choose CSV file'}
          </FileButton>
          {file ? <span className="truncate text-[0.8125rem] text-gray-700 dark:text-gray-300">{file.name}</span> : null}
        </div>

        <div aria-live="polite" className="flex flex-col gap-3">
          {fileError ? (
            <Alert tone="red" live="alert">
              {fileError}
            </Alert>
          ) : null}
          {preview.isError ? (
            <Alert tone="red" live="alert">
              Could not read the file. {errorMessage(preview.error)}
            </Alert>
          ) : null}
          {commit.isError ? (
            <Alert tone="red" live="alert">
              {errorMessage(commit.error)}
            </Alert>
          ) : null}
          {result ? (
            <>
              <p className="flex flex-wrap items-center gap-2 text-sm text-gray-700 dark:text-gray-300">
                <Badge color="green">{result.valid} ready</Badge>
                {result.invalid > 0 ? <Badge color="red">{result.invalid} with errors</Badge> : null}
                {result.invalid > 0 ? <span>Fix the rows below and choose the file again.</span> : null}
              </p>
              {result.rows.length ? (
                <div className="max-h-80 overflow-y-auto rounded-md ring-1 ring-gray-200 dark:ring-white/10">
                  <Table minWidth={560} aria-label="Import preview">
                    <THead>
                      <tr>
                        <Th className="text-right">Row</Th>
                        <Th>Name</Th>
                        <Th>Email</Th>
                        <Th>Level</Th>
                        <Th>Status</Th>
                      </tr>
                    </THead>
                    <TBody>
                      {result.rows.map((r) => (
                        <Tr key={r.row} className={cn(r.errors.length > 0 && 'bg-red-50/60 dark:bg-red-400/5')}>
                          <Td numeric>{r.row}</Td>
                          <Td>{[r.firstName, r.lastName].filter(Boolean).join(' ') || '—'}</Td>
                          <Td>{r.email || '—'}</Td>
                          <Td>{r.level || 'Member'}</Td>
                          <Td>
                            {r.errors.length ? (
                              <ul className="flex flex-col gap-0.5 text-xs font-medium text-red-700 dark:text-red-400">
                                {r.errors.map((e) => (
                                  <li key={e}>{e}</li>
                                ))}
                              </ul>
                            ) : (
                              <Badge color="green">OK</Badge>
                            )}
                          </Td>
                        </Tr>
                      ))}
                    </TBody>
                  </Table>
                </div>
              ) : (
                <p className="text-sm text-gray-600 dark:text-gray-400">The file has a header but no rows.</p>
              )}
            </>
          ) : null}
        </div>
      </div>
    </Dialog>
  )
}
