import { useId, useState } from 'react'
import { Controller, useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { isApiError } from '@/app/api'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { Textarea } from '@/components/ui/Input'
import { SegmentedControl } from '@/components/ui/SegmentedControl'
import { useToast } from '@/components/ui/toast-context'
import { usePromotePersonMutation } from '../api'
import { fullName } from '../format'
import { allowedTargets, isDemotion, levelLabels, promoteFormSchema, type Level, type Person, type PromoteFormValues } from '../schemas'

function becameText(name: string, level: Level): string {
  if (level === 'Board') return `${name} is now on the Board.`
  return `${name} is now ${level === 'Apprentice' ? 'an' : 'a'} ${level}.`
}

type Props = { person: Person; initialLevel: Level; onClose: () => void }

/** Change a person's level: the next step on the path, a Board seat, or a demotion (reason required). */
export function PromoteDialog({ person, initialLevel, onClose }: Props) {
  const formId = useId()
  const levelLabelId = useId()
  const { toast } = useToast()
  const mutation = usePromotePersonMutation()
  const [formError, setFormError] = useState<string | null>(null)
  const name = fullName(person)
  const options = allowedTargets(person.level).map((l) => ({ value: l, label: levelLabels[l] }))
  const {
    register,
    control,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<PromoteFormValues>({ resolver: zodResolver(promoteFormSchema), defaultValues: { toLevel: initialLevel, reason: '' } })

  const toLevel = useWatch({ control, name: 'toLevel' })
  const demotion = isDemotion(person.level, toLevel)
  const leavesSeat = demotion && (person.level === 'Board' || person.level === 'Goon')
  const createsLogin = person.accountStatus === 'NoLogin' && person.level === 'Member' && !demotion

  const onSubmit = handleSubmit(({ toLevel: to, reason }) => {
    if (mutation.isPending) return
    if (isDemotion(person.level, to) && !reason) {
      setError('reason', { type: 'required', message: 'Enter a reason. Moving someone down needs one.' })
      return
    }
    setFormError(null)
    mutation.mutate(
      { id: person.id, body: { toLevel: to, reason: reason || null } },
      {
        onSuccess: () => {
          toast({
            tone: 'success',
            title: becameText(name, to),
            description: createsLogin ? 'An invite email was sent. The link expires in 48 hours.' : undefined,
          })
          onClose()
        },
        onError: (e) => {
          const reasonError = isApiError(e, 400) ? e.fieldErrors.reason : undefined
          if (reasonError) setError('reason', { type: 'server', message: reasonError })
          else setFormError(errorMessage(e))
        },
      },
    )
  })

  return (
    <Dialog
      open
      onClose={onClose}
      title={demotion ? 'Change level' : `Promote ${name}`}
      description={`${name} is ${person.level === 'Board' ? 'on the Board' : `${person.level === 'Apprentice' ? 'an' : 'a'} ${person.level}`}. Every change is kept in their history.`}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form={formId} variant={demotion ? 'destructive' : 'primary'} loading={mutation.isPending}>
            {demotion ? `Move to ${toLevel}` : `Promote to ${toLevel}`}
          </Button>
        </>
      }
    >
      <form id={formId} noValidate onSubmit={onSubmit} className="flex flex-col gap-4">
        {formError ? (
          <Alert tone="red" live="alert">
            {formError}
          </Alert>
        ) : null}
        <div className="flex flex-col gap-1.5">
          <span id={levelLabelId} className="text-sm font-medium text-gray-700 dark:text-gray-200">
            New level
          </span>
          <Controller
            control={control}
            name="toLevel"
            render={({ field }) => (
              <SegmentedControl variant="chips" options={options} value={field.value} onChange={field.onChange} aria-labelledby={levelLabelId} />
            )}
          />
        </div>
        <Field label="Reason" optional={!demotion} error={errors.reason?.message} hint={demotion ? 'Required when moving someone down.' : undefined}>
          {(p) => <Textarea {...p} {...register('reason')} rows={3} />}
        </Field>
        <div aria-live="polite" className="flex flex-col gap-2">
          {createsLogin ? (
            <Alert tone="gray">Creates their login and emails an invite. The link expires in 48 hours.</Alert>
          ) : null}
          {toLevel === 'Board' ? <Alert tone="gray">Board accounts must turn on 2-step verification when they first sign in.</Alert> : null}
          {leavesSeat ? (
            <Alert tone="yellow">Positions that no longer fit the new level are vacated, and CFO or finance assistant roles are cleared.</Alert>
          ) : null}
        </div>
      </form>
    </Dialog>
  )
}
