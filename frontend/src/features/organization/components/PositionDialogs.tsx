import { useId, useState, type FormEvent } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { isApiError } from '@/app/api'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { Checkbox, Input, Select, Textarea } from '@/components/ui/Input'
import { SegmentedControl } from '@/components/ui/SegmentedControl'
import { Skeleton } from '@/components/ui/Spinner'
import { useToast } from '@/components/ui/toast-context'
import {
  useAssignPositionMutation,
  useCreatePositionMutation,
  usePeopleQuery,
  useUpdatePositionMutation,
  useVacatePositionMutation,
} from '../api'
import { fullName } from '../format'
import { kindLabels, levelForKind, positionFormSchema, type Position, type PositionFormValues, type PositionKind } from '../schemas'

const kindOptions = (['BoardSeat', 'GoonPosition'] as const).map((k) => ({ value: k, label: kindLabels[k] }))

function DialogError({ message }: { message: string | null }) {
  return message ? (
    <Alert tone="red" live="alert">
      {message}
    </Alert>
  ) : null
}

export function NewPositionDialog({ initialKind, onClose }: { initialKind: PositionKind; onClose: () => void }) {
  const formId = useId()
  const kindLabelId = useId()
  const { toast } = useToast()
  const mutation = useCreatePositionMutation()
  const [formError, setFormError] = useState<string | null>(null)
  const {
    register,
    control,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<PositionFormValues>({ resolver: zodResolver(positionFormSchema), defaultValues: { kind: initialKind, title: '' } })

  const onSubmit = handleSubmit((body) => {
    if (mutation.isPending) return
    setFormError(null)
    mutation.mutate(body, {
      onSuccess: () => {
        toast({ tone: 'success', title: `${body.title} added.` })
        onClose()
      },
      onError: (e) => {
        const titleError = isApiError(e, 400) ? e.fieldErrors.title : undefined
        if (titleError) setError('title', { type: 'server', message: titleError })
        else setFormError(errorMessage(e))
      },
    })
  })

  return (
    <Dialog
      open
      onClose={onClose}
      title="New position"
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form={formId} loading={mutation.isPending}>
            Add position
          </Button>
        </>
      }
    >
      <form id={formId} noValidate onSubmit={onSubmit} className="flex flex-col gap-4">
        <DialogError message={formError} />
        <div className="flex flex-col gap-1.5">
          <span id={kindLabelId} className="text-sm font-medium text-gray-700 dark:text-gray-200">
            Type
          </span>
          <Controller
            control={control}
            name="kind"
            render={({ field }) => <SegmentedControl options={kindOptions} value={field.value} onChange={field.onChange} aria-labelledby={kindLabelId} />}
          />
        </div>
        <Field label="Title" error={errors.title?.message}>
          {(p) => <Input {...p} {...register('title')} placeholder="e.g. Head of Logistics" autoComplete="off" />}
        </Field>
      </form>
    </Dialog>
  )
}

export function EditPositionDialog({ position, onClose }: { position: Position; onClose: () => void }) {
  const formId = useId()
  const { toast } = useToast()
  const mutation = useUpdatePositionMutation()
  const [title, setTitle] = useState(position.title)
  const [isActive, setIsActive] = useState(position.isActive)
  const [titleError, setTitleError] = useState<string | undefined>()
  const [formError, setFormError] = useState<string | null>(null)

  function submit(e: FormEvent) {
    e.preventDefault()
    if (mutation.isPending) return
    const parsed = positionFormSchema.shape.title.safeParse(title)
    if (!parsed.success) {
      setTitleError(parsed.error.issues[0]?.message)
      return
    }
    setTitleError(undefined)
    mutation.mutate(
      { id: position.id, body: { title: parsed.data, isActive } },
      {
        onSuccess: () => {
          toast({ tone: 'success', title: 'Position saved.' })
          onClose()
        },
        onError: (err) => setFormError(errorMessage(err)),
      },
    )
  }

  return (
    <Dialog
      open
      onClose={onClose}
      title="Edit position"
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form={formId} loading={mutation.isPending}>
            Save
          </Button>
        </>
      }
    >
      <form id={formId} noValidate onSubmit={submit} className="flex flex-col gap-4">
        <DialogError message={formError} />
        <Field label="Title" error={titleError}>
          {(p) => <Input {...p} value={title} onChange={(e) => setTitle(e.target.value)} autoComplete="off" />}
        </Field>
        <label className="flex min-h-10 items-center gap-2.5 text-sm text-gray-700 dark:text-gray-200">
          <Checkbox checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
          Active (inactive positions are hidden and can't be assigned)
        </label>
      </form>
    </Dialog>
  )
}

/** Assign or change the holder. The picker lists active people at the level the position needs. */
export function AssignDialog({ position, positions, onClose }: { position: Position; positions: Position[]; onClose: () => void }) {
  const formId = useId()
  const { toast } = useToast()
  const mutation = useAssignPositionMutation()
  const level = levelForKind[position.kind]
  const people = usePeopleQuery({ level, page: 1, pageSize: 100 })
  const [personId, setPersonId] = useState(position.holder?.id ?? '')
  const [pickError, setPickError] = useState<string | undefined>()
  const [formError, setFormError] = useState<string | null>(null)

  // A person holds at most one board seat: note who already sits elsewhere.
  const seatOf = new Map<string, string>()
  if (position.kind === 'BoardSeat') {
    for (const p of positions) if (p.kind === 'BoardSeat' && p.holder && p.id !== position.id) seatOf.set(p.holder.id, p.title)
  }
  const candidates = (people.data?.items ?? []).filter((p) => p.isActive)

  function submit(e: FormEvent) {
    e.preventDefault()
    if (mutation.isPending) return
    if (!personId) {
      setPickError(`Choose a ${level === 'Board' ? 'board member' : 'goon'}.`)
      return
    }
    setPickError(undefined)
    setFormError(null)
    const chosen = candidates.find((p) => p.id === personId)
    mutation.mutate(
      { id: position.id, personId },
      {
        onSuccess: () => {
          toast({ tone: 'success', title: chosen ? `${fullName(chosen)} is now ${position.title}.` : `${position.title} assigned.` })
          onClose()
        },
        onError: (err) => setFormError(errorMessage(err)),
      },
    )
  }

  return (
    <Dialog
      open
      onClose={onClose}
      size="sm"
      title={position.holder ? `Change ${position.title}` : `Assign ${position.title}`}
      description={position.kind === 'BoardSeat' ? 'Board seats go to board members. One seat per person.' : 'Goon positions go to goons.'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form={formId} loading={mutation.isPending} disabled={people.isPending}>
            Assign
          </Button>
        </>
      }
    >
      <form id={formId} noValidate onSubmit={submit} className="flex flex-col gap-4">
        <DialogError message={formError} />
        {people.isPending ? (
          <div role="status" aria-label="Loading people">
            <Skeleton className="h-10 w-full" />
          </div>
        ) : people.isError ? (
          <Alert
            tone="red"
            live="alert"
            action={
              <Button variant="secondary" size="sm" onClick={() => void people.refetch()}>
                Try again
              </Button>
            }
          >
            Could not load people. {errorMessage(people.error)}
          </Alert>
        ) : candidates.length === 0 ? (
          <Alert tone="gray">
            No active {level === 'Board' ? 'board members' : 'goons'} yet. Promote someone first on the People tab.
          </Alert>
        ) : (
          <Field label={level === 'Board' ? 'Board member' : 'Goon'} error={pickError}>
            {(p) => (
              <Select {...p} value={personId} onChange={(e) => setPersonId(e.target.value)}>
                <option value="">Choose a person</option>
                {candidates.map((c) => (
                  <option key={c.id} value={c.id} disabled={seatOf.has(c.id)}>
                    {fullName(c)}
                    {seatOf.has(c.id) ? ` (holds ${seatOf.get(c.id)})` : ''}
                  </option>
                ))}
              </Select>
            )}
          </Field>
        )}
      </form>
    </Dialog>
  )
}

export function VacateDialog({ position, onClose }: { position: Position; onClose: () => void }) {
  const formId = useId()
  const { toast } = useToast()
  const mutation = useVacatePositionMutation()
  const [reason, setReason] = useState('')
  const holder = position.holder

  function submit(e: FormEvent) {
    e.preventDefault()
    if (mutation.isPending) return
    mutation.mutate(
      { id: position.id, reason: reason.trim() || null },
      {
        onSuccess: () => {
          toast({ tone: 'success', title: `${position.title} is now vacant.` })
          onClose()
        },
        onError: (err) => {
          toast({ tone: 'error', title: 'Could not vacate the position.', description: errorMessage(err) })
          onClose()
        },
      },
    )
  }

  return (
    <Dialog
      open
      onClose={onClose}
      role="alertdialog"
      size="sm"
      title={`Vacate ${position.title}?`}
      description={holder ? `${holder.firstName} ${holder.lastName} leaves this position. It is kept in their history.` : undefined}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form={formId} variant="destructive" loading={mutation.isPending}>
            Vacate
          </Button>
        </>
      }
    >
      <form id={formId} noValidate onSubmit={submit}>
        <Field label="Reason" optional>
          {(p) => <Textarea {...p} value={reason} onChange={(e) => setReason(e.target.value)} maxLength={500} rows={3} />}
        </Field>
      </form>
    </Dialog>
  )
}
