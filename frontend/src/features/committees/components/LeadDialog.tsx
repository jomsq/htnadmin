import { useId, useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { isApiError } from '@/app/api'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { Select } from '@/components/ui/Input'
import { useToast } from '@/components/ui/toast-context'
import { useSetLeadMutation } from '../api'
import { leadFormSchema, personName, type Committee, type CommitteePerson, type LeadFormInput } from '../schemas'

/** Only an active goon can lead a committee: the picker lists goons on this committee and in the pool. */
export function LeadDialog({
  eventId,
  committee,
  pool,
  onClose,
}: {
  eventId: string
  committee: Committee
  pool: CommitteePerson[]
  onClose: () => void
}) {
  const formId = useId()
  const { toast } = useToast()
  const setLead = useSetLeadMutation(eventId)
  const [formError, setFormError] = useState<string | null>(null)
  const {
    register,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<LeadFormInput>({ resolver: zodResolver(leadFormSchema), defaultValues: { personId: '' } })

  const byId = new Map<string, CommitteePerson>()
  for (const p of [...committee.members, ...pool]) {
    if (p.level === 'Goon' && p.personId !== committee.lead?.personId) byId.set(p.personId, p)
  }
  const candidates = [...byId.values()].sort((a, b) => personName(a).localeCompare(personName(b)))

  function save(personId: string | null, title: string) {
    if (setLead.isPending) return
    setFormError(null)
    setLead.mutate(
      { committeeId: committee.id, personId },
      {
        onSuccess: () => {
          toast({ tone: 'success', title })
          onClose()
        },
        onError: (e) => {
          if (isApiError(e) && (e.code === 'wrong_level' || e.code === 'already_lead')) {
            setError('personId', { type: 'server', message: errorMessage(e) })
            return
          }
          setFormError(errorMessage(e))
        },
      },
    )
  }

  const onSubmit = handleSubmit(({ personId }) => {
    const person = candidates.find((c) => c.personId === personId)
    save(personId, `${person ? personName(person) : 'The lead'} now leads ${committee.name}.`)
  })

  return (
    <Dialog
      open
      onClose={onClose}
      size="sm"
      title={committee.lead ? `Change the lead of ${committee.name}` : `Assign a lead for ${committee.name}`}
      description="The lead can assign and score tasks in this committee."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          {committee.lead ? (
            <Button variant="danger" loading={setLead.isPending} onClick={() => save(null, `${committee.name} has no lead now.`)}>
              Remove lead
            </Button>
          ) : null}
          <Button type="submit" form={formId} loading={setLead.isPending} disabled={candidates.length === 0}>
            {committee.lead ? 'Change lead' : 'Assign lead'}
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
        {committee.lead ? (
          <Alert tone="gray">
            {personName(committee.lead)} leads it now. They stay on the committee as a member.
          </Alert>
        ) : null}
        {candidates.length === 0 ? (
          <Alert tone="yellow">No goon is available for this event. Add a goon to the committee first.</Alert>
        ) : (
          <Field label="Committee lead" error={errors.personId?.message} hint="Goons only.">
            {(p) => (
              <Select {...p} {...register('personId')}>
                <option value="">Pick a goon</option>
                {candidates.map((c) => (
                  <option key={c.personId} value={c.personId}>
                    {personName(c)}
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
