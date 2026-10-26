import { useId } from 'react'
import { Controller, useForm, useWatch, type Control } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Field } from '@/components/ui/Field'
import { Input } from '@/components/ui/Input'
import { Skeleton } from '@/components/ui/Spinner'
import { Stepper } from '@/components/ui/Stepper'
import { Switch } from '@/components/ui/Switch'
import { useToast } from '@/components/ui/toast-context'
import { useOrgSettingsQuery, useSavePointsMutation } from '../api'
import { defaultPoints, pointsFormSchema, PREVIEW_BASE, previewPoints, type PointsSettings } from '../schemas'
import { SettingsCard } from './SettingsCard'

const multipliers = [
  { name: 'leadMultiplier', label: 'Lead', hint: 'Owns the task.' },
  { name: 'collabMultiplier', label: 'Collab', hint: 'Works on it with the lead.' },
  { name: 'assistantMultiplier', label: 'Assistant', hint: 'Helps with parts of it.' },
] as const

const presets = [
  { name: 'small', label: 'Small' },
  { name: 'medium', label: 'Medium' },
  { name: 'large', label: 'Large' },
  { name: 'xl', label: 'XL' },
] as const

export function PointsSection() {
  const query = useOrgSettingsQuery()
  return (
    <SettingsCard
      id="points"
      title="Points"
      description="Task points = base points × role multiplier, awarded when a task is approved into Done. A task can have several people in each role."
    >
      {query.isPending ? (
        <div role="status" aria-label="Loading points settings" className="flex flex-col gap-3">
          <Skeleton className="h-11 w-full" />
          <Skeleton className="h-11 w-full" />
          <Skeleton className="h-11 w-full" />
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
          Could not load points settings. {errorMessage(query.error)}
        </Alert>
      ) : (
        <PointsForm initial={query.data.points} />
      )}
    </SettingsCard>
  )
}

function Preview({ control, name }: { control: Control<PointsSettings>; name: (typeof multipliers)[number]['name'] }) {
  const value = useWatch({ control, name })
  return (
    <span aria-live="polite" className="text-[0.8125rem] text-gray-600 tabular-nums dark:text-gray-400">
      {PREVIEW_BASE}-pt task → <span className="font-semibold text-gray-900 dark:text-gray-50">{previewPoints(value)}</span>
    </span>
  )
}

function PointsForm({ initial }: { initial: PointsSettings }) {
  const { toast } = useToast()
  const mutation = useSavePointsMutation()
  const approvalLabelId = useId()
  const approvalHintId = useId()
  const {
    control,
    register,
    handleSubmit,
    reset,
    formState: { errors, isDirty },
  } = useForm<PointsSettings>({ resolver: zodResolver(pointsFormSchema), defaultValues: initial })

  const onSubmit = handleSubmit((body) => {
    if (mutation.isPending) return
    mutation.mutate(body, {
      onSuccess: () => {
        reset(body)
        toast({ tone: 'success', title: 'Points settings saved.', description: 'New multipliers apply to tasks approved from now on.' })
      },
      onError: (e) => toast({ tone: 'error', title: 'Could not save points settings.', description: errorMessage(e) }),
    })
  })

  return (
    <form noValidate onSubmit={onSubmit} className="flex flex-col gap-6">
      <fieldset className="flex flex-col gap-4">
        <legend className="mb-3 text-[0.8125rem] font-semibold tracking-wide text-gray-700 uppercase dark:text-gray-300">Role multipliers</legend>
        {multipliers.map((m) => (
          <Controller
            key={m.name}
            control={control}
            name={m.name}
            render={({ field, fieldState }) => (
              <Field label={`${m.label} multiplier`} hint={`${m.hint} 0 to 2, in steps of 0.1.`} error={fieldState.error?.message}>
                {(p) => (
                  <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
                    <Stepper
                      {...p}
                      name={`${m.label} multiplier`}
                      value={field.value}
                      onChange={field.onChange}
                      onBlur={field.onBlur}
                      min={0}
                      max={2}
                      step={0.1}
                      precision={1}
                    />
                    <Preview control={control} name={m.name} />
                  </div>
                )}
              </Field>
            )}
          />
        ))}
      </fieldset>

      <fieldset className="flex flex-col gap-3">
        <legend className="mb-3 text-[0.8125rem] font-semibold tracking-wide text-gray-700 uppercase dark:text-gray-300">Base point presets</legend>
        <div className="grid grid-cols-2 gap-3.5 sm:grid-cols-4">
          {presets.map((p) => (
            <Field key={p.name} label={p.label} error={errors.presets?.[p.name]?.message}>
              {(c) => <Input {...c} {...register(`presets.${p.name}`, { valueAsNumber: true })} type="number" inputMode="numeric" min={1} max={1000} step={1} className="tabular-nums" />}
            </Field>
          ))}
        </div>
        <p className="text-xs text-gray-600 dark:text-gray-400">Whole points from 1 to 1000, each larger than the one before.</p>
      </fieldset>

      <div className="flex items-start justify-between gap-4 rounded-md p-4 ring-1 ring-gray-200 dark:ring-white/10">
        <div className="flex flex-col gap-0.5">
          <span id={approvalLabelId} className="text-sm font-medium text-gray-900 dark:text-gray-50">
            Require approval
          </span>
          <span id={approvalHintId} className="text-[0.8125rem] text-gray-600 dark:text-gray-400">
            Points are awarded only when the committee lead or an admin approves the task into Done.
          </span>
        </div>
        <Controller
          control={control}
          name="requireTaskApproval"
          render={({ field }) => (
            <Switch checked={field.value} onChange={field.onChange} aria-labelledby={approvalLabelId} aria-describedby={approvalHintId} />
          )}
        />
      </div>

      <div className="flex flex-wrap items-center gap-3">
        <Button type="submit" loading={mutation.isPending} disabled={!isDirty}>
          Save points
        </Button>
        <Button variant="secondary" onClick={() => reset(defaultPoints, { keepDefaultValues: true })}>
          Reset to defaults
        </Button>
        <span className="text-xs text-gray-600 dark:text-gray-400">Defaults: Lead 1.0, Collab 0.7, Assistant 0.3.</span>
      </div>
    </form>
  )
}
