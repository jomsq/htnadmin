import { z } from 'zod'

// ---- Board (phase4-contract.md, section 4B) ---------------------------------

export const TASK_STATUSES = ['Backlog', 'ToDo', 'InProgress', 'Review', 'Done'] as const
export const taskStatusSchema = z.enum(TASK_STATUSES)
export type TaskStatus = (typeof TASK_STATUSES)[number]

/** Column names exactly as the canvas writes them. */
export const STATUS_LABELS: Record<TaskStatus, string> = {
  Backlog: 'Backlog',
  ToDo: 'To do',
  InProgress: 'In progress',
  Review: 'Review',
  Done: 'Done',
}

/** Column dots (canvas frame 07): gray, darker gray, brand, amber, green. */
export const STATUS_DOTS: Record<TaskStatus, string> = {
  Backlog: 'bg-gray-400',
  ToDo: 'bg-gray-600 dark:bg-gray-400',
  InProgress: 'bg-brand-600 dark:bg-brand-300',
  Review: 'bg-yellow-600 dark:bg-yellow-400',
  Done: 'bg-green-700 dark:bg-green-400',
}

export const TASK_ROLES = ['Lead', 'Collab', 'Assistant'] as const
export const taskRoleSchema = z.enum(TASK_ROLES)
export type TaskRole = (typeof TASK_ROLES)[number]

export const cardAssigneeSchema = z.object({
  name: z.string(),
  initials: z.string(),
  role: taskRoleSchema,
  isVolunteer: z.boolean().default(false),
})
export type CardAssignee = z.infer<typeof cardAssigneeSchema>

export const taskCardSchema = z.object({
  id: z.string(),
  reference: z.string(),
  title: z.string(),
  committeeId: z.string(),
  committeeName: z.string(),
  status: taskStatusSchema.optional(),
  dueAt: z.string().nullish(),
  dueSoon: z.boolean().default(false),
  overdue: z.boolean().default(false),
  checklistDone: z.number().default(0),
  checklistTotal: z.number().default(0),
  basePoints: z.number().default(0),
  labels: z.array(z.string()).default([]),
  sortOrder: z.number().default(0),
  /** Server-sent: Tasks.Work on this task. The server enforces it again. */
  canMove: z.boolean().default(false),
  isMine: z.boolean().default(false),
  assignees: z.array(cardAssigneeSchema).default([]),
})
export type TaskCard = z.infer<typeof taskCardSchema>

export const boardColumnSchema = z.object({
  status: taskStatusSchema,
  /** The real number of cards in the column, even when `cards` only holds part of them. */
  count: z.number().default(0),
  /** True when the server capped the column: Done returns only the newest 50 cards. */
  hasMore: z.boolean().default(false),
  cards: z.array(taskCardSchema).default([]),
})
export type BoardColumn = z.infer<typeof boardColumnSchema>

export const boardCommitteeSchema = z.object({
  id: z.string(),
  name: z.string(),
  canManage: z.boolean().default(false),
})
export type BoardCommittee = z.infer<typeof boardCommitteeSchema>

export const taskBoardSchema = z.object({
  canCreateTask: z.boolean().default(false),
  committees: z.array(boardCommitteeSchema).default([]),
  columns: z.array(boardColumnSchema).default([]),
})
export type TaskBoard = z.infer<typeof taskBoardSchema>

// ---- Detail ------------------------------------------------------------------

export const taskAssigneeSchema = z.object({
  id: z.string(),
  personId: z.string().nullish(),
  volunteerId: z.string().nullish(),
  name: z.string(),
  initials: z.string(),
  role: taskRoleSchema,
  multiplier: z.number().default(0),
  points: z.number().default(0),
  isVolunteer: z.boolean().default(false),
})
export type TaskAssignee = z.infer<typeof taskAssigneeSchema>

export const checklistItemSchema = z.object({
  id: z.string(),
  text: z.string(),
  isDone: z.boolean().default(false),
  sortOrder: z.number().default(0),
})
export type ChecklistItem = z.infer<typeof checklistItemSchema>

export const attachmentSchema = z.object({
  id: z.string(),
  fileName: z.string(),
  contentType: z.string(),
  sizeBytes: z.number().default(0),
  uploadedAt: z.string(),
  uploadedByName: z.string().nullish(),
  url: z.string(),
})
export type TaskAttachment = z.infer<typeof attachmentSchema>

export const commentSchema = z.object({
  id: z.string(),
  authorName: z.string().nullish(),
  authorInitials: z.string(),
  body: z.string(),
  mentions: z.array(z.object({ personId: z.string(), name: z.string() })).default([]),
  createdAt: z.string(),
  editedAt: z.string().nullish(),
  isDeleted: z.boolean().default(false),
  canEdit: z.boolean().default(false),
})
export type TaskComment = z.infer<typeof commentSchema>

export const activitySchema = z.object({
  at: z.string(),
  /** Null when the actor's account was removed; the log row stays. */
  actorName: z.string().nullish(),
  actorInitials: z.string(),
  kind: z.string(),
  text: z.string(),
})
export type TaskActivityEntry = z.infer<typeof activitySchema>

export const awardSchema = z.object({
  name: z.string(),
  role: taskRoleSchema,
  points: z.number(),
  awardedAt: z.string(),
  reversedAt: z.string().nullish(),
})

export const taskDetailSchema = z.object({
  id: z.string(),
  reference: z.string(),
  eventId: z.string(),
  eventName: z.string(),
  committeeId: z.string(),
  committeeName: z.string(),
  title: z.string(),
  description: z.string().nullish(),
  status: taskStatusSchema,
  basePoints: z.number().default(0),
  dueAt: z.string().nullish(),
  labels: z.array(z.string()).default([]),
  isArchived: z.boolean().default(false),
  archiveReason: z.string().nullish(),
  createdAt: z.string(),
  createdByName: z.string().nullish(),
  assignedByName: z.string().nullish(),
  approvedAt: z.string().nullish(),
  approvedByName: z.string().nullish(),
  requireTaskApproval: z.boolean().default(true),
  multipliers: z.object({ lead: z.number(), collab: z.number(), assistant: z.number() }),
  presets: z.object({ small: z.number(), medium: z.number(), large: z.number(), xl: z.number() }),
  canEdit: z.boolean().default(false),
  canWork: z.boolean().default(false),
  canApprove: z.boolean().default(false),
  canComment: z.boolean().default(false),
  /**
   * The caller manages this live, not-Done task but is its only Lead, so `canApprove` is false only because
   * nobody signs off their own work. Another committee lead or a board member has to approve it.
   */
  needsOtherApprover: z.boolean().default(false),
  /** Manage rights on a Done, not archived task: the approval can be undone and the awards reversed. */
  canReopen: z.boolean().default(false),
  /** Manage rights on a live task that holds no live awards (a card with awards must be reopened first). */
  canArchive: z.boolean().default(false),
  /** Manage rights on an archived task: it can go back on the board. */
  canRestore: z.boolean().default(false),
  assignees: z.array(taskAssigneeSchema).default([]),
  checklist: z.array(checklistItemSchema).default([]),
  attachments: z.array(attachmentSchema).default([]),
  comments: z.array(commentSchema).default([]),
  activity: z.array(activitySchema).default([]),
  awards: z.array(awardSchema).default([]),
})
export type TaskDetail = z.infer<typeof taskDetailSchema>

// ---- My tasks ----------------------------------------------------------------

export const myTasksSchema = z.object({
  openCount: z.number().default(0),
  groups: z
    .array(
      z.object({
        eventId: z.string(),
        eventName: z.string(),
        tasks: z.array(taskCardSchema).default([]),
      }),
    )
    .default([]),
})
export type MyTasks = z.infer<typeof myTasksSchema>

// ---- Forms -------------------------------------------------------------------

const optionalTrimmed = (max: number, message: string) =>
  z
    .string()
    .trim()
    .max(max, message)
    .transform((v) => (v === '' ? undefined : v))

export const taskFormSchema = z.object({
  committeeId: z.string().min(1, 'Pick a committee.'),
  title: z.string().trim().min(1, 'Enter a task title.').max(140, 'Use 140 characters or fewer.'),
  description: optionalTrimmed(4000, 'Use 4000 characters or fewer.'),
  dueAt: optionalTrimmed(40, 'Enter a valid date.'),
  basePoints: z.coerce.number().int('Use whole points.').min(0, 'Points start at 0.').max(1000, 'Points stop at 1000.'),
  status: taskStatusSchema,
})
export type TaskFormInput = z.input<typeof taskFormSchema>
export type TaskFormValues = z.output<typeof taskFormSchema>

export const checklistFormSchema = z.object({
  text: z.string().trim().min(1, 'Enter a checklist item.').max(200, 'Use 200 characters or fewer.'),
})
export type ChecklistFormInput = z.input<typeof checklistFormSchema>

export const commentFormSchema = z.object({
  body: z.string().trim().min(1, 'Write a comment first.').max(4000, 'Use 4000 characters or fewer.'),
})
export type CommentFormInput = z.input<typeof commentFormSchema>

export const reasonFormSchema = z.object({
  reason: z.string().trim().min(1, 'Enter a reason.').max(500, 'Use 500 characters or fewer.'),
})
export type ReasonFormInput = z.input<typeof reasonFormSchema>

export const assigneeFormSchema = z.object({
  /** "person:{id}" or "volunteer:{id}" so one <select> can offer both. */
  who: z.string().min(1, 'Pick a person or volunteer.'),
  role: taskRoleSchema,
})
export type AssigneeFormInput = z.input<typeof assigneeFormSchema>

// ---- Display helpers ----------------------------------------------------------

/** "40" and "28", never "28.00" — points are 2 dp on the wire. */
export function formatPoints(n: number): string {
  return Number.isInteger(n) ? String(n) : String(Number(n.toFixed(2)))
}

export function formatMultiplier(n: number): string {
  return n.toFixed(1)
}

/** Projected award for a role; volunteers never earn points. */
export function projectedPoints(basePoints: number, multiplier: number, isVolunteer: boolean): number {
  if (isVolunteer) return 0
  return Number((basePoints * multiplier).toFixed(2))
}

export function multiplierFor(role: TaskRole, m: { lead: number; collab: number; assistant: number }): number {
  return role === 'Lead' ? m.lead : role === 'Collab' ? m.collab : m.assistant
}

export const ATTACHMENT_ACCEPT = 'image/png,image/jpeg,application/pdf'
export const ATTACHMENT_MAX_BYTES = 5 * 1024 * 1024

/** "1.2 MB", "840 KB". */
export function formatFileSize(bytes: number): string {
  if (bytes >= 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
  if (bytes >= 1024) return `${Math.round(bytes / 1024)} KB`
  return `${bytes} B`
}

/** Short type name next to a file chip: "PDF", "PNG", "JPG". */
export function fileTypeLabel(contentType: string, fileName: string): string {
  if (contentType === 'application/pdf') return 'PDF'
  if (contentType === 'image/png') return 'PNG'
  if (contentType === 'image/jpeg') return 'JPG'
  const ext = fileName.split('.').pop()
  return ext ? ext.toUpperCase() : 'File'
}

/** Client-side check before upload; the server checks the magic bytes too. */
export function attachmentError(file: File): string | undefined {
  const name = file.name.toLowerCase()
  const okType = ATTACHMENT_ACCEPT.split(',').includes(file.type)
  const okExt = /\.(png|jpe?g|pdf)$/.test(name)
  if (!okType || !okExt) return 'Attach a PNG, JPG or PDF file.'
  if (file.size > ATTACHMENT_MAX_BYTES) return 'This file is larger than 5 MB. Attach a smaller file.'
  return undefined
}
