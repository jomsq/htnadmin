import { useEffect, useId, useRef, useState } from 'react'
import { useParams } from 'react-router'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { isApiError } from '@/app/api'
import { errorMessage } from '@/app/errors'
import { PublicHeader, PublicLayout } from '@/app/layouts/PublicLayout'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { cardClass } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { Field } from '@/components/ui/Field'
import { FileButton } from '@/components/ui/FileButton'
import { Input } from '@/components/ui/Input'
import { Skeleton } from '@/components/ui/Spinner'
import { CheckIcon, LockIcon, UploadIcon } from '@/components/ui/icons'
import { formatEventDateTime } from '@/features/events/format'
import { cn } from '@/lib/cn'
import { formatPeso } from '@/lib/format'
import { useProofInfoQuery, useUploadProofMutation } from '../api'
import { checkProofFile, proofReferenceSchema, type ProofInfo, type ProofReferenceValues } from '../schemas'

const INVALID_LINK = 'This upload link is invalid or has expired. Reply to the billing email instead, or ask the organizers for help.'

/** Public proof-of-payment upload /pay/:token. Anonymous; the token is the only key. */
export function ProofUploadPage() {
  const { token = '' } = useParams()
  const query = useProofInfoQuery(token)

  if (query.isPending) {
    return (
      <PublicLayout header={<PublicHeader title="Proof of payment" />}>
        <div role="status" aria-label="Loading" className="flex flex-col gap-3 p-5">
          <Skeleton className="h-24 w-full" />
          <Skeleton className="h-12 w-full" />
        </div>
      </PublicLayout>
    )
  }

  if (query.isError) {
    const gone = isApiError(query.error, 404) || isApiError(query.error, 410)
    return (
      <PublicLayout header={<PublicHeader title="Proof of payment" />}>
        {gone ? (
          <EmptyState icon={<LockIcon size={22} />} title="Link not valid" description={INVALID_LINK} />
        ) : (
          <div className="p-5">
            <Alert
              tone="red"
              live="alert"
              action={
                <Button variant="secondary" size="sm" onClick={() => void query.refetch()}>
                  Try again
                </Button>
              }
            >
              Could not load this page. {errorMessage(query.error)}
            </Alert>
          </div>
        )}
      </PublicLayout>
    )
  }

  return (
    <PublicLayout header={<PublicHeader title={query.data.eventName} subtitle="Proof of payment" />}>
      <ProofUpload token={token} info={query.data} />
    </PublicLayout>
  )
}

function ProofUpload({ token, info }: { token: string; info: ProofInfo }) {
  const [done, setDone] = useState(false)
  const [replacing, setReplacing] = useState(false)
  const headingRef = useRef<HTMLHeadingElement>(null)

  useEffect(() => {
    if (done) headingRef.current?.focus()
  }, [done])

  return (
    <div className="flex flex-col gap-5 px-5 pt-6 pb-8">
      <dl className={cn(cardClass, 'flex flex-col gap-2 p-4 text-sm')}>
        <div className="flex justify-between gap-4">
          <dt className="text-gray-600 dark:text-gray-400">Participant ID</dt>
          <dd className="font-mono font-semibold text-gray-900 dark:text-gray-50">{info.participantId}</dd>
        </div>
        <div className="flex justify-between gap-4">
          <dt className="text-gray-600 dark:text-gray-400">Amount due</dt>
          <dd className="font-semibold text-gray-900 tabular-nums dark:text-gray-50">{formatPeso(info.amountDue)}</dd>
        </div>
        {info.deadline ? (
          <div className="flex justify-between gap-4">
            <dt className="text-gray-600 dark:text-gray-400">Pay before</dt>
            <dd className="font-semibold text-gray-900 dark:text-gray-50">{formatEventDateTime(info.deadline)}</dd>
          </div>
        ) : null}
      </dl>

      {done ? (
        <section aria-labelledby="proof-done" className="flex flex-col items-center gap-3 text-center">
          <div aria-hidden="true" className="flex size-14 items-center justify-center rounded-full bg-green-50 text-green-700 dark:bg-green-500/10 dark:text-green-400">
            <CheckIcon size={28} />
          </div>
          <h2 id="proof-done" ref={headingRef} tabIndex={-1} className="text-xl font-semibold text-gray-900 outline-none dark:text-gray-50">
            Proof received
          </h2>
          <p role="status" className="text-[0.9375rem] leading-relaxed text-gray-600 dark:text-gray-400">
            Thanks. Our team checks payments by hand and emails your ticket with QR code once it&apos;s confirmed.
          </p>
          <Button
            variant="secondary"
            size="lg"
            onClick={() => {
              setDone(false)
              setReplacing(true)
            }}
          >
            Upload a different file
          </Button>
        </section>
      ) : info.uploaded && !replacing ? (
        <section className="flex flex-col gap-3">
          <Alert tone="green">We already have your proof of payment. Our team will confirm it soon and email your ticket.</Alert>
          <Button variant="secondary" size="lg" onClick={() => setReplacing(true)}>
            Replace the uploaded proof
          </Button>
        </section>
      ) : (
        <ProofForm token={token} replace={info.uploaded || replacing} onDone={() => setDone(true)} />
      )}
    </div>
  )
}

function ProofForm({ token, replace, onDone }: { token: string; replace: boolean; onDone: () => void }) {
  const fileHintId = useId()
  const fileErrorId = useId()
  const upload = useUploadProofMutation(token)
  const [file, setFile] = useState<File | null>(null)
  const [fileError, setFileError] = useState<string | null>(null)
  const [formError, setFormError] = useState<string | null>(null)
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<ProofReferenceValues>({ resolver: zodResolver(proofReferenceSchema), defaultValues: { reference: '' } })

  function onFile(f: File) {
    const problem = checkProofFile(f)
    setFileError(problem)
    setFile(problem ? null : f)
  }

  const onSubmit = handleSubmit(
    ({ reference }) => {
      if (upload.isPending) return
      if (!file) {
        setFileError('Choose a screenshot or photo of your receipt.')
        return
      }
      setFormError(null)
      upload.mutate(
        { file, reference },
        {
          onSuccess: onDone,
          onError: (e) => {
            if (isApiError(e, 404) || isApiError(e, 410)) setFormError(INVALID_LINK)
            else if (isApiError(e, 400) || isApiError(e, 413)) setFileError(errorMessage(e, { 400: 'Upload a PNG or JPG image up to 5 MB.', 413: 'The image is larger than 5 MB.' }))
            else setFormError(errorMessage(e))
          },
        },
      )
    },
    () => {
      if (!file) setFileError('Choose a screenshot or photo of your receipt.')
    },
  )

  return (
    <form noValidate onSubmit={onSubmit} className="flex flex-col gap-5">
      <h2 className="text-base font-semibold text-gray-900 dark:text-gray-50">{replace ? 'Replace your proof of payment' : 'Upload your proof of payment'}</h2>
      {formError ? (
        <Alert tone="red" live="alert">
          {formError}
        </Alert>
      ) : null}

      <div className="flex flex-col gap-2">
        <span className="text-sm font-medium text-gray-700 dark:text-gray-200">Screenshot or photo of the receipt</span>
        <div className="flex flex-wrap items-center gap-3">
          <FileButton
            accept="image/png,image/jpeg,.png,.jpg,.jpeg"
            onFile={onFile}
            size="lg"
            aria-describedby={[fileHintId, fileError ? fileErrorId : ''].filter(Boolean).join(' ')}
          >
            <UploadIcon size={16} />
            {file ? 'Choose another image' : 'Choose image'}
          </FileButton>
          {file ? <span className="min-w-0 truncate text-sm text-gray-700 dark:text-gray-300">{file.name}</span> : null}
        </div>
        <span id={fileHintId} className="text-xs text-gray-600 dark:text-gray-400">
          PNG or JPG, up to 5 MB.
        </span>
        {fileError ? (
          <p id={fileErrorId} role="alert" className="text-xs font-medium text-red-700 dark:text-red-400">
            {fileError}
          </p>
        ) : null}
      </div>

      <Field label="Reference number" error={errors.reference?.message} hint="From your GCash or bank receipt.">
        {(p) => <Input {...p} {...register('reference')} controlSize="xl" maxLength={60} autoComplete="off" spellCheck={false} />}
      </Field>

      <Button type="submit" size="lg" className="h-13 text-base" loading={upload.isPending}>
        {replace ? 'Replace proof of payment' : 'Upload proof of payment'}
      </Button>
    </form>
  )
}
