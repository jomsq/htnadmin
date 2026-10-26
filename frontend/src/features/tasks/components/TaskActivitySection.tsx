import { useId, useMemo, useRef, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { isApiError } from '@/app/api'
import { errorMessage } from '@/app/errors'
import { Avatar } from '@/components/ui/Avatar'
import { Button } from '@/components/ui/Button'
import { Textarea } from '@/components/ui/Input'
import { useToast } from '@/components/ui/toast-context'
import { useCommitteesQuery } from '@/features/committees/api'
import { personName } from '@/features/committees/schemas'
import { formatEventDateTime } from '@/features/events/format'
import { cn } from '@/lib/cn'
import { useAddCommentMutation } from '../api'
import { commentFormSchema, type CommentFormInput, type TaskDetail } from '../schemas'

type Candidate = { personId: string; name: string }

/** The @mention list: people on a committee of this event (they can see the board). */
function useMentionCandidates(eventId: string): Candidate[] {
  const committees = useCommitteesQuery(eventId)
  return useMemo(() => {
    const byId = new Map<string, Candidate>()
    for (const c of committees.data?.committees ?? []) {
      for (const p of [c.lead, ...c.members]) {
        if (p) byId.set(p.personId, { personId: p.personId, name: personName(p) })
      }
    }
    return [...byId.values()].sort((a, b) => a.name.localeCompare(b.name))
  }, [committees.data])
}

/** The "@mika" fragment the caret sits in, if any. */
function mentionQuery(body: string, caret: number): { query: string; start: number } | null {
  const upToCaret = body.slice(0, caret)
  const match = /@([\p{L}\p{N} '-]{0,40})$/u.exec(upToCaret)
  if (!match) return null
  // Only treat it as a mention while the fragment is short (a name, not a sentence).
  if (match[1].split(/\s+/).length > 2) return null
  return { query: match[1].trim().toLowerCase(), start: match.index }
}

/** Comment box with @mention autocomplete, plus the activity log (canvas frame 08). */
export function TaskActivitySection({ task }: { task: TaskDetail }) {
  const fieldId = useId()
  const listId = useId()
  const { toast } = useToast()
  const addComment = useAddCommentMutation(task.id)
  const candidates = useMentionCandidates(task.eventId)
  const textareaRef = useRef<HTMLTextAreaElement | null>(null)
  const [caret, setCaret] = useState(0)
  const [dismissed, setDismissed] = useState(false)
  const [active, setActive] = useState(0)
  const [picked, setPicked] = useState<Candidate[]>([])
  // The server re-checks every mention against the people who can see the board (400 on mentionPersonIds).
  const [mentionError, setMentionError] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    control,
    setValue,
    reset,
    formState: { errors },
  } = useForm<CommentFormInput>({ resolver: zodResolver(commentFormSchema), defaultValues: { body: '' } })
  const body = useWatch({ control, name: 'body' }) ?? ''
  const { ref: registerRef, ...bodyField } = register('body')

  const fragment = mentionQuery(body, caret)
  const matches = fragment
    ? candidates.filter((c) => c.name.toLowerCase().includes(fragment.query)).slice(0, 6)
    : []
  const open = !!fragment && matches.length > 0 && !dismissed

  function syncCaret(el: HTMLTextAreaElement) {
    setCaret(el.selectionStart ?? 0)
    setDismissed(false)
  }

  function insert(candidate: Candidate) {
    if (!fragment) return
    setMentionError(null)
    const before = body.slice(0, fragment.start)
    const after = body.slice(caret)
    const next = `${before}@${candidate.name} ${after}`
    setValue('body', next, { shouldValidate: true })
    setPicked((all) => (all.some((p) => p.personId === candidate.personId) ? all : [...all, candidate]))
    setActive(0)
    const position = before.length + candidate.name.length + 2
    window.requestAnimationFrame(() => {
      const el = textareaRef.current
      if (!el) return
      el.focus()
      el.setSelectionRange(position, position)
      setCaret(position)
    })
  }

  const onSubmit = handleSubmit(({ body: value }) => {
    if (addComment.isPending) return
    setMentionError(null)
    const text = value.trim()
    // Only mentions still written in the comment are sent; the server emails them.
    const mentionPersonIds = picked.filter((p) => text.includes(`@${p.name}`)).map((p) => p.personId)
    addComment.mutate(
      { body: text, mentionPersonIds },
      {
        onSuccess: () => {
          reset({ body: '' })
          setPicked([])
          toast({ tone: 'success', title: 'Comment added.' })
        },
        onError: (e) => {
          // A mention the server refuses belongs on the mention field, not in a toast that leaves no trace.
          const onMentions = isApiError(e, 400) ? e.fieldErrors.mentionPersonIds : undefined
          if (onMentions) {
            // role="alert" announces it; the comment text is kept so the mention can be fixed.
            setMentionError(onMentions)
            return
          }
          toast({ tone: 'error', title: 'Could not add the comment.', description: errorMessage(e) })
        },
      },
    )
  })

  return (
    <section className="flex flex-col gap-3">
      <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-50">Activity</h3>

      {task.canComment ? (
        <form noValidate onSubmit={onSubmit} className="flex flex-col gap-2">
          <label htmlFor={fieldId} className="text-xs font-medium text-gray-700 dark:text-gray-200">
            Write a comment
          </label>
          <div className="relative">
            <Textarea
              {...bodyField}
              ref={(el) => {
                textareaRef.current = el
                registerRef(el)
              }}
              id={fieldId}
              rows={2}
              placeholder="Write a comment. Use @ to mention someone."
              aria-invalid={errors.body || mentionError ? true : undefined}
              aria-describedby={
                [errors.body ? `${fieldId}-error` : null, mentionError ? `${fieldId}-mentions` : null].filter(Boolean).join(' ') || undefined
              }
              role="combobox"
              aria-expanded={open}
              aria-controls={open ? listId : undefined}
              aria-autocomplete="list"
              aria-activedescendant={open ? `${listId}-${active}` : undefined}
              onSelect={(e) => syncCaret(e.currentTarget)}
              onKeyUp={(e) => {
                if (e.key !== 'Escape') syncCaret(e.currentTarget)
              }}
              onClick={(e) => syncCaret(e.currentTarget)}
              onKeyDown={(e) => {
                if (!open) return
                if (e.key === 'ArrowDown') {
                  e.preventDefault()
                  setActive((i) => (i + 1) % matches.length)
                } else if (e.key === 'ArrowUp') {
                  e.preventDefault()
                  setActive((i) => (i - 1 + matches.length) % matches.length)
                } else if (e.key === 'Enter') {
                  e.preventDefault()
                  insert(matches[active])
                } else if (e.key === 'Escape') {
                  e.preventDefault()
                  setDismissed(true)
                }
              }}
            />
            {open ? (
              <ul
                id={listId}
                role="listbox"
                aria-label="Mention someone"
                className="absolute z-20 mt-1 flex w-64 flex-col rounded-md bg-white p-1 shadow-lg ring-1 ring-gray-900/10 dark:bg-gray-900 dark:ring-white/10"
              >
                {matches.map((c, i) => (
                  <li key={c.personId} id={`${listId}-${i}`} role="option" aria-selected={i === active}>
                    <button
                      type="button"
                      onClick={() => insert(c)}
                      className={cn(
                        'flex min-h-10 w-full items-center rounded-md px-2.5 text-left text-sm text-gray-900 hover:bg-gray-100 dark:text-gray-50 dark:hover:bg-white/5',
                        i === active && 'bg-gray-100 dark:bg-white/5',
                      )}
                    >
                      {c.name}
                    </button>
                  </li>
                ))}
              </ul>
            ) : null}
          </div>
          {errors.body ? (
            <p id={`${fieldId}-error`} className="text-xs font-medium text-red-700 dark:text-red-400">
              {errors.body.message}
            </p>
          ) : null}
          {mentionError ? (
            <p id={`${fieldId}-mentions`} role="alert" className="text-xs font-medium text-red-700 dark:text-red-400">
              Mentions: {mentionError}
            </p>
          ) : null}
          <div className="flex items-center gap-3">
            <Button type="submit" variant="secondary" loading={addComment.isPending}>
              Comment
            </Button>
            <p className="text-xs text-gray-600 dark:text-gray-400">Mentioned people get an email with a link to this task.</p>
          </div>
        </form>
      ) : null}

      {task.activity.length === 0 ? <p className="text-[0.8125rem] text-gray-600 dark:text-gray-400">Nothing has happened yet.</p> : null}
      <ul className="flex flex-col gap-3">
        {task.activity.map((entry, i) => (
          <li key={`${entry.at}-${i}`} className="flex gap-2.5">
            <Avatar initials={entry.actorInitials} />
            <div className="flex flex-col gap-0.5 text-sm">
              <span className="text-gray-900 dark:text-gray-50">
                <strong className="font-semibold">{entry.actorName ?? 'Someone'}</strong> <span className="text-gray-600 dark:text-gray-400">{entry.text}</span>
              </span>
              <span className="text-xs text-gray-600 dark:text-gray-400">{formatEventDateTime(entry.at)}</span>
            </div>
          </li>
        ))}
      </ul>
    </section>
  )
}
