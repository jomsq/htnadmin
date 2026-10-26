import { useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { isApiError } from '@/app/api'
import { errorMessage } from '@/app/errors'
import { contrastWithWhite, isHexColor, MIN_CONTRAST } from '@/app/theme/brand'
import { Alert } from '@/components/ui/Alert'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Field } from '@/components/ui/Field'
import { FileButton } from '@/components/ui/FileButton'
import { Input } from '@/components/ui/Input'
import { Skeleton } from '@/components/ui/Spinner'
import { CheckIcon, UploadIcon } from '@/components/ui/icons'
import { useToast } from '@/components/ui/toast-context'
import { cn } from '@/lib/cn'
import { useOrgSettingsQuery, useSaveBrandingMutation, useUploadLogoMutation, type LogoVariant } from '../api'
import { brandingFormSchema, type Branding, type BrandingFormValues } from '../schemas'
import { SettingsCard } from './SettingsCard'

/** Preset colors; every one has at least 4.5:1 contrast with white text. */
const swatches = [
  { hex: '#B71C1C', name: 'HTN red' },
  { hex: '#BE185D', name: 'Pink' },
  { hex: '#B45309', name: 'Amber' },
  { hex: '#047857', name: 'Green' },
  { hex: '#0F766E', name: 'Teal' },
  { hex: '#1D4ED8', name: 'Blue' },
  { hex: '#374151', name: 'Gray' },
] as const

const LOGO_MAX_BYTES = 1024 * 1024

function checkLogo(file: File): string | null {
  if (file.type !== 'image/png' && !/\.png$/i.test(file.name)) return 'Upload a PNG file.'
  if (file.size > LOGO_MAX_BYTES) return 'The logo is larger than 1 MB.'
  return null
}

export function BrandingSection() {
  const query = useOrgSettingsQuery()
  return (
    <SettingsCard
      id="branding"
      title="Email branding & org theme"
      description="Logo and brand color for emails and this app. The email layout is fixed."
    >
      {query.isPending ? (
        <div role="status" aria-label="Loading branding" className="flex flex-col gap-3">
          <Skeleton className="h-11 w-full" />
          <Skeleton className="h-40 w-full" />
        </div>
      ) : query.isError ? (
        <Alert
          tone="red"
          live="alert"
          action={
            <Button variant="secondary" size="sm" onClick={() => void query.refetch()}>
              Try again
            </Button>
          }
        >
          Could not load branding. {errorMessage(query.error)}
        </Alert>
      ) : (
        <BrandingForm branding={query.data.branding} />
      )}
    </SettingsCard>
  )
}

function BrandingForm({ branding }: { branding: Branding }) {
  const { toast } = useToast()
  const mutation = useSaveBrandingMutation()
  const {
    register,
    control,
    handleSubmit,
    setValue,
    setError,
    reset,
    formState: { errors, isDirty },
  } = useForm<BrandingFormValues>({
    resolver: zodResolver(brandingFormSchema),
    defaultValues: { brandColor: branding.brandColor.toUpperCase(), paymentDeadlineHours: branding.paymentDeadlineHours },
  })
  const color = useWatch({ control, name: 'brandColor' }).trim()
  const deadline = useWatch({ control, name: 'paymentDeadlineHours' })
  const valid = isHexColor(color)
  const ratio = valid ? contrastWithWhite(color) : null
  const lowContrast = ratio !== null && ratio < MIN_CONTRAST
  const previewColor = valid && !lowContrast ? color : branding.brandColor

  const onSubmit = handleSubmit((body) => {
    if (mutation.isPending) return
    const next = { ...body, brandColor: body.brandColor.toUpperCase() }
    mutation.mutate(next, {
      onSuccess: () => {
        reset(next)
        toast({ tone: 'success', title: 'Branding saved.', description: 'The brand color now applies across the app and in emails.' })
      },
      onError: (e) => {
        const fieldError = isApiError(e, 400) ? e.fieldErrors : {}
        if (fieldError.brandColor) setError('brandColor', { type: 'server', message: fieldError.brandColor })
        else if (fieldError.paymentDeadlineHours) setError('paymentDeadlineHours', { type: 'server', message: fieldError.paymentDeadlineHours })
        else toast({ tone: 'error', title: 'Could not save branding.', description: errorMessage(e) })
      },
    })
  })

  return (
    <div className="flex flex-col gap-6">
      <form noValidate onSubmit={onSubmit} className="flex flex-col gap-5">
        <fieldset className="flex flex-col gap-3">
          <legend className="mb-3 text-sm font-medium text-gray-700 dark:text-gray-200">Brand color</legend>
          <div role="radiogroup" aria-label="Brand color presets" className="flex flex-wrap gap-2">
            {swatches.map((s) => {
              const selected = color.toUpperCase() === s.hex
              return (
                <button
                  key={s.hex}
                  type="button"
                  role="radio"
                  aria-checked={selected}
                  aria-label={`${s.name} ${s.hex}`}
                  title={`${s.name} ${s.hex}`}
                  onClick={() => setValue('brandColor', s.hex, { shouldDirty: true, shouldValidate: true })}
                  className={cn(
                    'inline-flex size-11 items-center justify-center rounded-md text-white ring-offset-2 dark:ring-offset-gray-900',
                    selected ? 'ring-2 ring-gray-900 dark:ring-gray-50' : 'ring-1 ring-gray-900/10 dark:ring-white/15',
                  )}
                  style={{ backgroundColor: s.hex }}
                >
                  {selected ? <CheckIcon size={18} /> : null}
                </button>
              )
            })}
          </div>
          <div className="flex flex-wrap items-start gap-4">
            <Field label="Hex" error={errors.brandColor?.message} className="w-40">
              {(p) => <Input {...p} {...register('brandColor')} maxLength={7} spellCheck={false} autoComplete="off" className="font-mono uppercase" />}
            </Field>
            <div aria-live="polite" className="flex min-h-10 flex-1 flex-col justify-end gap-2 pt-7 text-[0.8125rem]">
              {ratio !== null ? (
                lowContrast ? (
                  <Alert tone="red">
                    Low contrast: {ratio.toFixed(2)}:1 with white text. Buttons and links would be hard to read. Pick a darker shade (4.5:1 or
                    more).
                  </Alert>
                ) : (
                  <span className="flex items-center gap-2 text-gray-700 dark:text-gray-300">
                    <Badge color="green">Passes AA</Badge>
                    Contrast {ratio.toFixed(2)}:1 with white text
                  </span>
                )
              ) : null}
            </div>
          </div>
        </fieldset>

        <Field label="Payment deadline (hours)" error={errors.paymentDeadlineHours?.message} hint="Shown in billing emails. 1 to 336 hours; default 48." className="w-56">
          {(p) => <Input {...p} {...register('paymentDeadlineHours', { valueAsNumber: true })} type="number" inputMode="numeric" min={1} max={336} className="tabular-nums" />}
        </Field>

        <div>
          <Button type="submit" loading={mutation.isPending} disabled={!isDirty || lowContrast || !valid}>
            Save branding
          </Button>
        </div>
      </form>

      <div className="grid gap-4 sm:grid-cols-2">
        <LogoTile variant="light" url={branding.logoLightUrl} />
        <LogoTile variant="dark" url={branding.logoDarkUrl} />
      </div>

      <EmailPreview color={previewColor} logoUrl={branding.logoLightUrl} deadlineHours={Number.isFinite(deadline) ? deadline : branding.paymentDeadlineHours} />
    </div>
  )
}

function LogoTile({ variant, url }: { variant: LogoVariant; url: string | null }) {
  const { toast } = useToast()
  const upload = useUploadLogoMutation()
  const [error, setError] = useState<string | null>(null)
  const label = variant === 'light' ? 'Logo for light backgrounds' : 'Logo for dark backgrounds'

  function onFile(file: File) {
    const problem = checkLogo(file)
    setError(problem)
    if (problem) return
    upload.mutate(
      { variant, file },
      {
        onSuccess: () => toast({ tone: 'success', title: variant === 'light' ? 'Light logo uploaded.' : 'Dark logo uploaded.' }),
        onError: (e) => setError(errorMessage(e, { 400: 'Upload a PNG file up to 1 MB.' })),
      },
    )
  }

  return (
    <div className="flex flex-col gap-2.5">
      <span className="text-sm font-medium text-gray-700 dark:text-gray-200">{label}</span>
      <div
        className={cn(
          'flex h-24 items-center justify-center rounded-md ring-1 ring-inset',
          variant === 'light' ? 'bg-white ring-gray-200' : 'bg-gray-900 ring-white/10',
        )}
      >
        {url ? (
          <img src={url} alt={`Current ${label.toLowerCase()}`} className="max-h-16 max-w-[80%] object-contain" />
        ) : (
          <span className={cn('text-[0.8125rem]', variant === 'light' ? 'text-gray-600' : 'text-gray-400')}>No logo yet</span>
        )}
      </div>
      <div className="flex flex-wrap items-center gap-2">
        <FileButton accept="image/png,.png" onFile={onFile} loading={upload.isPending} size="sm">
          <UploadIcon size={14} />
          {url ? `Replace ${variant} logo` : `Upload ${variant} logo`}
        </FileButton>
        <span className="text-xs text-gray-600 dark:text-gray-400">PNG, up to 1 MB</span>
      </div>
      {error ? (
        <p role="alert" className="text-xs font-medium text-red-700 dark:text-red-400">
          {error}
        </p>
      ) : null}
    </div>
  )
}

/** Live preview of the fixed billing email layout with the chosen color and light logo. */
function EmailPreview({ color, logoUrl, deadlineHours }: { color: string; logoUrl: string | null; deadlineHours: number }) {
  return (
    <section aria-labelledby="email-preview-heading" className="flex flex-col gap-2.5">
      <h3 id="email-preview-heading" className="text-sm font-medium text-gray-700 dark:text-gray-200">
        Email preview
      </h3>
      <div className="rounded-md bg-gray-100 p-4 dark:bg-white/5">
        {/* Emails are always light, so the preview stays light in dark mode too. */}
        <div className="mx-auto flex max-w-md flex-col overflow-hidden rounded-md bg-white text-gray-900 shadow-sm ring-1 ring-gray-900/5">
          <div className="flex h-14 items-center border-b border-gray-200 px-5">
            {logoUrl ? (
              <img src={logoUrl} alt="" className="max-h-8 object-contain" />
            ) : (
              <span className="text-sm font-semibold">HackTheNorth.PH</span>
            )}
          </div>
          <div className="flex flex-col gap-3 px-5 py-5 text-sm">
            <p className="text-base font-semibold">Complete your registration</p>
            <p className="text-gray-700">
              Hi Ana, thanks for registering for HTN Con 2026. Pay within <span className="font-semibold">{deadlineHours} hours</span> using
              your participant ID <span className="font-mono">HTN-7KQ2-M9XD</span> as the reference.
            </p>
            <span className="inline-flex h-10 w-fit items-center rounded-md px-4 text-sm font-semibold text-white" style={{ backgroundColor: color }}>
              View payment details
            </span>
            <p className="text-xs text-gray-600">
              Questions? Reply to this email. <span style={{ color }} className="font-medium underline">hackthenorth.ph</span>
            </p>
          </div>
        </div>
      </div>
    </section>
  )
}
