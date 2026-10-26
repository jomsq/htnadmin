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

/** Role dots follow the canvas: Lead brand, Collab teal, Assistant amber. */
const multipliers = [
  { name: 'leadMultiplier', label: 'Lead', dot: 'bg-brand-600 dark:bg-brand-300' },
  { name: 'collabMultiplier', label: 'Collab', dot: 'bg-teal-600 dark:bg-teal-400' },
  { name: 'assistantMultiplier', label: 'Assistant', dot: 'bg-amber-600 dark:bg-amber-400' },
] as const

const presets = [
  { name: 'small', label: 'Small' },
  { name: 'medium', label: 'Medium' },
  { name: 'large', label: 'Large' },
  { name: 'xl', label: 'Extra large' },
] as const

export function PointsSection() {
  const query = useOrgSettingsQuery()
  return (
    <SettingsCard
      id="points"
      title="Points multipliers"
      description="Each person on a completed task earns base points × their role's multiplier."
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
      <fieldset className="grid grid-cols-[repeat(auto-fit,minmax(200px,1fr))] gap-4">
        <legend className="sr-only">Role multipliers (0 to 2, in steps of 0.1)</legend>
        {multipliers.map((m) => (
          <Controller
            key={m.name}
            control={control}
            name={m.name}
            render={({ field, fieldState }) => (
              <Field
                className="rounded-md p-4 shadow-sm ring-1 ring-gray-900/5 dark:ring-white/10"
                label={
                  <span className="inline-flex items-center gap-2 font-semibold text-gray-900 dark:text-gray-50">
                    <span aria-hidden="true" className={`size-2.5 rounded-full ${m.dot}`} />
                    {m.label}
                    <span className="sr-only"> multiplier</span>
                  </span>
                }
                error={fieldState.error?.message}
              >
                {(p) => (
                  <div className="flex flex-col gap-3 pt-1.5">
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

      <fieldset className="flex flex-col gap-3 border-t border-gray-100 pt-4 dark:border-white/10">
        <legend className="mb-1 text-sm font-semibold text-gray-900 dark:text-gray-50">Size presets</legend>
        <p className="text-[0.8125rem] text-gray-600 dark:text-gray-400">
          Shortcuts shown when a lead sets base points. They can still type any value.
        </p>
        <div className="grid grid-cols-2 gap-3.5 sm:grid-cols-4">
          {presets.map((p) => (
            <Field key={p.name} label={p.label} error={errors.presets?.[p.name]?.message}>
              {(c) => <Input {...c} {...register(`presets.${p.name}`, { valueAsNumber: true })} type="number" inputMode="numeric" min={1} max={1000} step={1} className="tabular-nums" />}
            </Field>
          ))}
        </div>
        <p className="text-xs text-gray-600 dark:text-gray-400">Whole points from 1 to 1000, each larger than the one before.</p>
      </fieldset>

      <div className="flex items-center justify-between gap-4 border-t border-gray-100 pt-4 dark:border-white/10">
        <div className="flex flex-col gap-0.5">
          <span id={approvalLabelId} className="text-sm font-medium text-gray-900 dark:text-gray-50">
            Require approval before awarding
          </span>
          <span id={approvalHintId} className="text-[0.8125rem] text-gray-600 dark:text-gray-400">
            Tasks wait in Review until the committee lead or an admin approves.
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
          Reset to 1.0 / 0.7 / 0.3
        </Button>
      </div>
    </form>
  )
}
