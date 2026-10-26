import { useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { isApiError } from '@/app/api'
import { errorMessage } from '@/app/errors'
import { contrastWithWhite, DEFAULT_BRAND, isHexColor, MIN_CONTRAST } from '@/app/theme/brand'
import { Alert } from '@/components/ui/Alert'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { ColorSwatches } from '@/components/ui/ColorSwatches'
import { Field } from '@/components/ui/Field'
import { FileButton } from '@/components/ui/FileButton'
import { Input } from '@/components/ui/Input'
import { Skeleton } from '@/components/ui/Spinner'
import { UploadIcon } from '@/components/ui/icons'
import { useToast } from '@/components/ui/toast-context'
import { cn } from '@/lib/cn'
import { useOrgSettingsQuery, useSaveBrandingMutation, useUploadLogoMutation, type LogoVariant } from '../api'
import { brandingFormSchema, type Branding } from '../schemas'
import { SettingsCard } from './SettingsCard'

const LOGO_MAX_BYTES = 1024 * 1024

const themeFormSchema = brandingFormSchema.pick({ brandColor: true })
type ThemeFormValues = { brandColor: string }
const deadlineFormSchema = brandingFormSchema.pick({ paymentDeadlineHours: true })
type DeadlineFormValues = { paymentDeadlineHours: number }

function checkLogo(file: File): string | null {
  if (file.type !== 'image/png' && !/\.png$/i.test(file.name)) return 'Upload a PNG file.'
  if (file.size > LOGO_MAX_BYTES) return 'The logo is larger than 1 MB.'
  return null
}

function LoadState({ label, query }: { label: string; query: ReturnType<typeof useOrgSettingsQuery> }) {
  if (query.isPending) {
    return (
      <div role="status" aria-label={`Loading ${label}`} className="flex flex-col gap-3">
        <Skeleton className="h-11 w-full" />
        <Skeleton className="h-24 w-full" />
      </div>
    )
  }
  return (
    <Alert
      tone="red"
      live="alert"
      action={
        <Button variant="secondary" size="sm" onClick={() => void query.refetch()}>
          Try again
        </Button>
      }
    >
      Could not load {label}. {errorMessage(query.error)}
    </Alert>
  )
}

/** "Organization theme · admins" block inside the Appearance card (canvas frame 15): brand color and logos. */
export function OrgThemeSection() {
  const query = useOrgSettingsQuery()
  return (
    <section aria-labelledby="org-theme-heading" className="flex flex-col gap-5">
      <h3 id="org-theme-heading" className="text-[0.8125rem] font-semibold tracking-wide text-gray-700 uppercase dark:text-gray-300">
        Organization theme · admins
      </h3>
      {query.isSuccess ? <ThemeForm branding={query.data.branding} /> : <LoadState label="the organization theme" query={query} />}
    </section>
  )
}

function ThemeForm({ branding }: { branding: Branding }) {
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
  } = useForm<ThemeFormValues>({
    resolver: zodResolver(themeFormSchema),
    defaultValues: { brandColor: branding.brandColor.toUpperCase() },
  })
  const color = useWatch({ control, name: 'brandColor' }).trim()
  const valid = isHexColor(color)
  const ratio = valid ? contrastWithWhite(color) : null
  const lowContrast = ratio !== null && ratio < MIN_CONTRAST

  const onSubmit = handleSubmit((values) => {
    if (mutation.isPending) return
    const next = { brandColor: values.brandColor.toUpperCase() }
    mutation.mutate(
      { ...next, paymentDeadlineHours: branding.paymentDeadlineHours },
      {
        onSuccess: () => {
          reset(next)
          toast({ tone: 'success', title: 'Branding saved.', description: 'The brand color now applies across the app and in emails.' })
        },
        onError: (e) => {
          const fieldError = isApiError(e, 400) ? e.fieldErrors : {}
          if (fieldError.brandColor) setError('brandColor', { type: 'server', message: fieldError.brandColor })
          else toast({ tone: 'error', title: 'Could not save branding.', description: errorMessage(e) })
        },
      },
    )
  })

  return (
    <div className="flex flex-col gap-5">
      <form noValidate onSubmit={onSubmit} className="flex flex-col gap-4">
        <fieldset className="flex flex-col gap-3">
          <legend className="mb-2.5 text-sm font-medium text-gray-900 dark:text-gray-50">Brand color</legend>
          <ColorSwatches
            aria-label="Brand color presets"
            value={color}
            onChange={(hex) => setValue('brandColor', hex, { shouldDirty: true, shouldValidate: true })}
          />
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

        <div className="grid gap-4 sm:grid-cols-2">
          <LogoTile variant="light" url={branding.logoLightUrl} />
          <LogoTile variant="dark" url={branding.logoDarkUrl} />
        </div>

        <div className="flex flex-wrap items-center gap-3">
          <Button type="submit" loading={mutation.isPending} disabled={!isDirty || lowContrast || !valid}>
            Save branding
          </Button>
          <Button
            variant="secondary"
            onClick={() => setValue('brandColor', DEFAULT_BRAND.toUpperCase(), { shouldDirty: true, shouldValidate: true })}
          >
            Reset to HTN red
          </Button>
        </div>
      </form>
    </div>
  )
}

function LogoTile({ variant, url }: { variant: LogoVariant; url: string | null }) {
  const { toast } = useToast()
  const upload = useUploadLogoMutation()
  const [error, setError] = useState<string | null>(null)
  const label = variant === 'light' ? 'Logo for light mode' : 'Logo for dark mode'

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
    <div className="flex flex-col gap-2">
      <span className="text-[0.8125rem] font-medium text-gray-700 dark:text-gray-200">{label}</span>
      <div
        className={cn(
          'flex h-16 items-center justify-center rounded-md border border-dashed',
          variant === 'light' ? 'border-gray-300 bg-white' : 'border-gray-600 bg-gray-900',
        )}
      >
        {url ? (
          <img src={url} alt={`Current ${label.toLowerCase()}`} className="max-h-12 max-w-[80%] object-contain" />
        ) : (
          <span className={cn('text-[0.8125rem] font-bold', variant === 'light' ? 'text-gray-900' : 'text-white')}>HackTheNorth.PH</span>
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

/** "Email branding" card: defaults for billing emails (deadline) with a live preview of the fixed layout. */
export function BrandingSection() {
  const query = useOrgSettingsQuery()
  return (
    <SettingsCard
      id="branding"
      title="Email branding"
      description="Default logo, theme color and accounts for new events. Each event can override them."
    >
      {query.isSuccess ? <DeadlineForm branding={query.data.branding} /> : <LoadState label="email branding" query={query} />}
    </SettingsCard>
  )
}

function DeadlineForm({ branding }: { branding: Branding }) {
  const { toast } = useToast()
  const mutation = useSaveBrandingMutation()
  const {
    register,
    control,
    handleSubmit,
    setError,
    reset,
    formState: { errors, isDirty },
  } = useForm<DeadlineFormValues>({
    resolver: zodResolver(deadlineFormSchema),
    defaultValues: { paymentDeadlineHours: branding.paymentDeadlineHours },
  })
  const deadline = useWatch({ control, name: 'paymentDeadlineHours' })

  const onSubmit = handleSubmit((values) => {
    if (mutation.isPending) return
    mutation.mutate(
      { brandColor: branding.brandColor.toUpperCase(), ...values },
      {
        onSuccess: () => {
          reset(values)
          toast({ tone: 'success', title: 'Email branding saved.' })
        },
        onError: (e) => {
          const fieldError = isApiError(e, 400) ? e.fieldErrors : {}
          if (fieldError.paymentDeadlineHours) setError('paymentDeadlineHours', { type: 'server', message: fieldError.paymentDeadlineHours })
          else toast({ tone: 'error', title: 'Could not save email branding.', description: errorMessage(e) })
        },
      },
    )
  })

  return (
    <div className="flex flex-col gap-6">
      <form noValidate onSubmit={onSubmit} className="flex flex-wrap items-end gap-4">
        <Field label="Payment deadline (hours)" error={errors.paymentDeadlineHours?.message} hint="Shown in billing emails. 1 to 336 hours; default 48." className="w-56">
          {(p) => <Input {...p} {...register('paymentDeadlineHours', { valueAsNumber: true })} type="number" inputMode="numeric" min={1} max={336} className="tabular-nums" />}
        </Field>
        <Button type="submit" loading={mutation.isPending} disabled={!isDirty} className="mb-6">
          Save email branding
        </Button>
      </form>
      <EmailPreview
        color={branding.brandColor}
        logoUrl={branding.logoLightUrl}
        deadlineHours={Number.isFinite(deadline) ? deadline : branding.paymentDeadlineHours}
      />
    </div>
  )
}

/** Live preview of the fixed billing email layout with the saved color and light logo. */
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
            {logoUrl ? <img src={logoUrl} alt="" className="max-h-8 object-contain" /> : <span className="text-sm font-semibold">HackTheNorth.PH</span>}
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
