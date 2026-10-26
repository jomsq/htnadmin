import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { api, apiVoid } from '@/app/api'
import {
  myTasksSchema,
  taskBoardSchema,
  taskDetailSchema,
  type TaskBoard,
  type TaskDetail,
  type TaskRole,
  type TaskStatus,
} from './schemas'

export type BoardParams = { committeeId?: string; mine?: boolean }

export const taskKeys = {
  all: ['tasks'] as const,
  boards: (eventId: string) => ['tasks', 'board', eventId] as const,
  board: (eventId: string, params: BoardParams) => ['tasks', 'board', eventId, { committeeId: params.committeeId ?? null, mine: !!params.mine }] as const,
  detail: (taskId: string) => ['tasks', 'detail', taskId] as const,
  mine: ['tasks', 'mine'] as const,
}

const taskPath = (taskId: string) => `/api/tasks/${encodeURIComponent(taskId)}`

function boardUrl(eventId: string, params: BoardParams): string {
  const search = new URLSearchParams()
  if (params.committeeId) search.set('committeeId', params.committeeId)
  if (params.mine) search.set('mine', 'true')
  const qs = search.toString()
  return `/api/events/${encodeURIComponent(eventId)}/tasks${qs ? `?${qs}` : ''}`
}

export function useTaskBoardQuery(eventId: string | undefined, params: BoardParams) {
  return useQuery({
    queryKey: taskKeys.board(eventId ?? '', params),
    queryFn: () => api(boardUrl(eventId ?? '', params), taskBoardSchema),
    enabled: !!eventId,
  })
}

export function useTaskQuery(taskId: string | undefined) {
  return useQuery({
    queryKey: taskKeys.detail(taskId ?? ''),
    queryFn: () => api(taskPath(taskId ?? ''), taskDetailSchema),
    enabled: !!taskId,
  })
}

/** Drives the sidebar count and /my-tasks. */
export function useMyTasksQuery(enabled = true) {
  return useQuery({
    queryKey: taskKeys.mine,
    queryFn: () => api('/api/tasks/mine', myTasksSchema),
    enabled,
    staleTime: 60_000,
  })
}

/** Anything that changes a task can change every board view, the detail and the sidebar count. */
function invalidateTask(queryClient: QueryClient, taskId?: string) {
  void queryClient.invalidateQueries({ queryKey: ['tasks', 'board'] })
  void queryClient.invalidateQueries({ queryKey: taskKeys.mine })
  if (taskId) void queryClient.invalidateQueries({ queryKey: taskKeys.detail(taskId) })
}

// ---- Board writes --------------------------------------------------------------

export type CreateTaskBody = {
  committeeId: string
  title: string
  description?: string
  status?: TaskStatus
  basePoints?: number
  dueAt?: string
  labels?: string[]
}

export function useCreateTaskMutation(eventId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: CreateTaskBody) =>
      api(`/api/events/${encodeURIComponent(eventId)}/tasks`, taskDetailSchema, { method: 'POST', body }),
    onSuccess: (detail) => {
      queryClient.setQueryData(taskKeys.detail(detail.id), detail)
      invalidateTask(queryClient)
    },
  })
}

/** Moves the card between columns in every cached board view before the server answers. */
function moveCardInBoard(board: TaskBoard, taskId: string, status: TaskStatus): TaskBoard {
  const card = board.columns.flatMap((c) => c.cards).find((c) => c.id === taskId)
  if (!card) return board
  return {
    ...board,
    columns: board.columns.map((column) => {
      if (column.status === status) {
        if (column.cards.some((c) => c.id === taskId)) return column
        const cards = [...column.cards, { ...card, status }]
        // `count` is the column's real total, which can be larger than `cards` (Done is capped), so it moves by one.
        return { ...column, cards, count: column.count + 1 }
      }
      const cards = column.cards.filter((c) => c.id !== taskId)
      return cards.length === column.cards.length ? column : { ...column, cards, count: Math.max(0, column.count - 1) }
    }),
  }
}

export type MoveTaskInput = { taskId: string; status: TaskStatus; sortOrder?: number }

/**
 * Optimistic move (the only optimistic update in the app, per the react-page skill):
 * every cached board for this event is updated, and restored when the server refuses.
 */
export function useMoveTaskMutation(eventId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ taskId, status, sortOrder }: MoveTaskInput) =>
      api(`${taskPath(taskId)}/move`, taskDetailSchema, { method: 'POST', body: sortOrder === undefined ? { status } : { status, sortOrder } }),
    onMutate: async ({ taskId, status }) => {
      await queryClient.cancelQueries({ queryKey: taskKeys.boards(eventId) })
      const snapshot = queryClient.getQueriesData<TaskBoard>({ queryKey: taskKeys.boards(eventId) })
      for (const [key, board] of snapshot) {
        if (board) queryClient.setQueryData(key, moveCardInBoard(board, taskId, status))
      }
      return { snapshot }
    },
    onError: (_error, _input, context) => {
      for (const [key, board] of context?.snapshot ?? []) queryClient.setQueryData(key, board)
    },
    onSuccess: (detail) => queryClient.setQueryData(taskKeys.detail(detail.id), detail),
    onSettled: (_detail, _error, { taskId }) => invalidateTask(queryClient, taskId),
  })
}

// ---- Detail writes ------------------------------------------------------------

/** Mutations that answer with the full detail: cache it and refresh the boards. */
function useDetailMutation<TVars>(taskId: string, run: (vars: TVars) => Promise<TaskDetail>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: run,
    onSuccess: (detail) => {
      queryClient.setQueryData(taskKeys.detail(taskId), detail)
      invalidateTask(queryClient)
    },
  })
}

export type UpdateTaskBody = {
  title: string
  description?: string
  dueAt?: string | null
  labels: string[]
  basePoints: number
  committeeId: string
}

export function useUpdateTaskMutation(taskId: string) {
  return useDetailMutation(taskId, (body: UpdateTaskBody) => api(taskPath(taskId), taskDetailSchema, { method: 'PUT', body }))
}

export function useApproveTaskMutation(taskId: string) {
  return useDetailMutation(taskId, () => api(`${taskPath(taskId)}/approve`, taskDetailSchema, { method: 'POST' }))
}

export function useReopenTaskMutation(taskId: string) {
  return useDetailMutation(taskId, ({ reason, status }: { reason: string; status?: TaskStatus }) =>
    api(`${taskPath(taskId)}/reopen`, taskDetailSchema, { method: 'POST', body: status ? { reason, status } : { reason } }),
  )
}

/** Puts an archived card back on the board; it answers with the detail, so no reason is needed. */
export function useRestoreTaskMutation(taskId: string) {
  return useDetailMutation(taskId, () => api(`${taskPath(taskId)}/restore`, taskDetailSchema, { method: 'POST' }))
}

/** Archiving takes the card off the board (204, no detail back). Refused while the task holds live awards. */
export function useArchiveTaskMutation(taskId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ reason }: { reason: string }) => apiVoid(`${taskPath(taskId)}/archive`, { method: 'POST', body: { reason } }),
    onSuccess: () => invalidateTask(queryClient, taskId),
  })
}

export function useAddAssigneeMutation(taskId: string) {
  return useDetailMutation(taskId, (body: { personId?: string; volunteerId?: string; role: TaskRole }) =>
    api(`${taskPath(taskId)}/assignees`, taskDetailSchema, { method: 'POST', body }),
  )
}

export function useRemoveAssigneeMutation(taskId: string) {
  return useDetailMutation(taskId, ({ assigneeId }: { assigneeId: string }) =>
    api(`${taskPath(taskId)}/assignees/${encodeURIComponent(assigneeId)}`, taskDetailSchema, { method: 'DELETE' }),
  )
}

export function useAddChecklistItemMutation(taskId: string) {
  return useDetailMutation(taskId, ({ text }: { text: string }) =>
    api(`${taskPath(taskId)}/checklist`, taskDetailSchema, { method: 'POST', body: { text } }),
  )
}

export function useUpdateChecklistItemMutation(taskId: string) {
  return useDetailMutation(taskId, ({ itemId, ...body }: { itemId: string; text?: string; isDone?: boolean }) =>
    api(`${taskPath(taskId)}/checklist/${encodeURIComponent(itemId)}`, taskDetailSchema, { method: 'PUT', body }),
  )
}

export function useRemoveChecklistItemMutation(taskId: string) {
  return useDetailMutation(taskId, ({ itemId }: { itemId: string }) =>
    api(`${taskPath(taskId)}/checklist/${encodeURIComponent(itemId)}`, taskDetailSchema, { method: 'DELETE' }),
  )
}

export function useReorderChecklistMutation(taskId: string) {
  return useDetailMutation(taskId, ({ itemIds }: { itemIds: string[] }) =>
    api(`${taskPath(taskId)}/checklist/reorder`, taskDetailSchema, { method: 'POST', body: { itemIds } }),
  )
}

export function useAddCommentMutation(taskId: string) {
  return useDetailMutation(taskId, ({ body, mentionPersonIds }: { body: string; mentionPersonIds: string[] }) =>
    api(`${taskPath(taskId)}/comments`, taskDetailSchema, { method: 'POST', body: { body, mentionPersonIds } }),
  )
}

export function useUploadAttachmentMutation(taskId: string) {
  return useDetailMutation(taskId, (file: File) => {
    const form = new FormData()
    form.append('file', file)
    return api(`${taskPath(taskId)}/attachments`, taskDetailSchema, { method: 'POST', body: form })
  })
}

export function useRemoveAttachmentMutation(taskId: string) {
  return useDetailMutation(taskId, ({ attachmentId }: { attachmentId: string }) =>
    api(`${taskPath(taskId)}/attachments/${encodeURIComponent(attachmentId)}/remove`, taskDetailSchema, { method: 'POST' }),
  )
}
