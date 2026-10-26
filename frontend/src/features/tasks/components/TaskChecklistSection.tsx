import { useId } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { errorMessage } from '@/app/errors'
import { Button } from '@/components/ui/Button'
import { Checkbox, Input } from '@/components/ui/Input'
import { ProgressBar } from '@/components/ui/ProgressBar'
import { CloseIcon } from '@/components/ui/icons'
import { useToast } from '@/components/ui/toast-context'
import { cn } from '@/lib/cn'
import {
  useAddChecklistItemMutation,
  useRemoveChecklistItemMutation,
  useReorderChecklistMutation,
  useUpdateChecklistItemMutation,
} from '../api'
import { checklistFormSchema, type ChecklistFormInput, type TaskDetail } from '../schemas'

function ArrowIcon({ up }: { up: boolean }) {
  return (
    <svg aria-hidden="true" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
      <path d={up ? 'M12 19V5M5 12l7-7 7 7' : 'M12 5v14M19 12l-7 7-7-7'} />
    </svg>
  )
}

/** Checklist with the progress bar (canvas frame 08): add, toggle, remove and reorder. */
export function TaskChecklistSection({ task }: { task: TaskDetail }) {
  const inputId = useId()
  const { toast } = useToast()
  const add = useAddChecklistItemMutation(task.id)
  const update = useUpdateChecklistItemMutation(task.id)
  const remove = useRemoveChecklistItemMutation(task.id)
  const reorder = useReorderChecklistMutation(task.id)
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<ChecklistFormInput>({ resolver: zodResolver(checklistFormSchema), defaultValues: { text: '' } })

  const items = task.checklist
  const done = items.filter((i) => i.isDone).length
  const pct = items.length ? Math.round((done / items.length) * 100) : 0
  const canWork = task.canWork && task.status !== 'Done'

  function fail(e: unknown) {
    toast({ tone: 'error', title: 'Could not update the checklist.', description: errorMessage(e) })
  }

  const onAdd = handleSubmit(({ text }) => {
    if (add.isPending) return
    add.mutate({ text: text.trim() }, { onSuccess: () => reset({ text: '' }), onError: fail })
  })

  function moveItem(index: number, delta: number) {
    const next = [...items]
    const target = index + delta
    if (target < 0 || target >= next.length) return
    ;[next[index], next[target]] = [next[target], next[index]]
    reorder.mutate({ itemIds: next.map((i) => i.id) }, { onError: fail })
  }

  return (
    <section aria-labelledby={`${inputId}-heading`} className="flex flex-col gap-2.5">
      <div className="flex items-center gap-3">
        <h3 id={`${inputId}-heading`} className="text-sm font-semibold text-gray-900 dark:text-gray-50">
          Checklist
        </h3>
        <span className="text-[0.8125rem] text-gray-600 dark:text-gray-400">
          {done} of {items.length}
        </span>
      </div>
      <ProgressBar value={pct} tone="green" label={`${done} of ${items.length} checklist items done`} />

      {items.length === 0 ? <p className="text-[0.8125rem] text-gray-600 dark:text-gray-400">No checklist items yet.</p> : null}

      <ul className="flex flex-col">
        {items.map((item, index) => (
          <li key={item.id} className="flex min-h-10 items-center gap-2.5">
            <label className="flex min-h-10 flex-1 items-center gap-2.5 text-sm">
              <Checkbox
                checked={item.isDone}
                disabled={!canWork || update.isPending}
                onChange={(e) => update.mutate({ itemId: item.id, isDone: e.target.checked }, { onError: fail })}
              />
              <span className={cn(item.isDone ? 'text-gray-600 line-through dark:text-gray-400' : 'text-gray-900 dark:text-gray-50')}>
                {item.text}
              </span>
            </label>
            {canWork ? (
              <>
                <button
                  type="button"
                  aria-label={`Move “${item.text}” up`}
                  disabled={index === 0 || reorder.isPending}
                  onClick={() => moveItem(index, -1)}
                  className="inline-flex size-9 items-center justify-center rounded-md text-gray-600 hover:bg-gray-100 disabled:opacity-40 dark:text-gray-400 dark:hover:bg-white/5"
                >
                  <ArrowIcon up />
                </button>
                <button
                  type="button"
                  aria-label={`Move “${item.text}” down`}
                  disabled={index === items.length - 1 || reorder.isPending}
                  onClick={() => moveItem(index, 1)}
                  className="inline-flex size-9 items-center justify-center rounded-md text-gray-600 hover:bg-gray-100 disabled:opacity-40 dark:text-gray-400 dark:hover:bg-white/5"
                >
                  <ArrowIcon up={false} />
                </button>
                <button
                  type="button"
                  aria-label={`Remove “${item.text}”`}
                  disabled={remove.isPending}
                  onClick={() => remove.mutate({ itemId: item.id }, { onError: fail })}
                  className="inline-flex size-9 items-center justify-center rounded-md text-gray-600 hover:bg-gray-100 hover:text-red-700 disabled:opacity-40 dark:text-gray-400 dark:hover:bg-white/5"
                >
                  <CloseIcon size={16} />
                </button>
              </>
            ) : null}
          </li>
        ))}
      </ul>

      {canWork ? (
        <form noValidate onSubmit={onAdd} className="flex flex-wrap items-start gap-2">
          <div className="flex min-w-48 flex-1 flex-col gap-1">
            <label htmlFor={inputId} className="text-xs font-medium text-gray-700 dark:text-gray-200">
              New checklist item
            </label>
            <Input
              id={inputId}
              {...register('text')}
              aria-invalid={errors.text ? true : undefined}
              aria-describedby={errors.text ? `${inputId}-error` : undefined}
              maxLength={200}
              autoComplete="off"
            />
            {errors.text ? (
              <p id={`${inputId}-error`} className="text-xs font-medium text-red-700 dark:text-red-400">
                {errors.text.message}
              </p>
            ) : null}
          </div>
          <Button type="submit" variant="secondary" loading={add.isPending} className="mt-5">
            Add item
          </Button>
        </form>
      ) : null}
    </section>
  )
}
