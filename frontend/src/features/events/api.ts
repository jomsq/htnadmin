import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, apiVoid } from '@/app/api'
import {
  eventDetailSchema,
  eventListSchema,
  type EventDetail,
  type EventInfoBody,
  type EventTab,
  type EventUpdateBody,
  type TicketBody,
  type VoucherBody,
} from './schemas'

export const eventKeys = {
  all: ['events'] as const,
  lists: ['events', 'list'] as const,
  list: (tab: EventTab) => ['events', 'list', { tab }] as const,
  detail: (id: string) => ['events', 'detail', id] as const,
}

const eventPath = (id: string) => `/api/events/${encodeURIComponent(id)}`

export function useEventsQuery(tab: EventTab) {
  return useQuery({
    queryKey: eventKeys.list(tab),
    queryFn: () => api(`/api/events?tab=${tab}`, eventListSchema),
  })
}

export function useEventQuery(id: string | undefined) {
  return useQuery({
    queryKey: eventKeys.detail(id ?? ''),
    queryFn: () => api(eventPath(id ?? ''), eventDetailSchema),
    enabled: !!id,
  })
}

/** After a write: store the fresh detail (if the server sent it) and refetch lists and the detail. */
function useEventWriteSettled() {
  const queryClient = useQueryClient()
  return async (id: string, detail?: EventDetail) => {
    if (detail) queryClient.setQueryData(eventKeys.detail(id), detail)
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: eventKeys.lists }),
      queryClient.invalidateQueries({ queryKey: eventKeys.detail(id) }),
    ])
  }
}

export function useCreateEventMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: EventInfoBody) => api('/api/events', eventDetailSchema, { method: 'POST', body }),
    onSuccess: (detail) => {
      queryClient.setQueryData(eventKeys.detail(detail.id), detail)
      return queryClient.invalidateQueries({ queryKey: eventKeys.lists })
    },
  })
}

export function useUpdateEventMutation(id: string) {
  const settled = useEventWriteSettled()
  return useMutation({
    mutationFn: (body: EventUpdateBody) => api(eventPath(id), eventDetailSchema, { method: 'PUT', body }),
    onSuccess: (detail) => settled(id, detail),
  })
}

export function usePublishEventMutation(id: string) {
  const settled = useEventWriteSettled()
  return useMutation({
    mutationFn: () => apiVoid(`${eventPath(id)}/publish`, { method: 'POST' }),
    onSuccess: () => settled(id),
  })
}

export function useCloseEventMutation(id: string) {
  const settled = useEventWriteSettled()
  return useMutation({
    mutationFn: () => apiVoid(`${eventPath(id)}/close`, { method: 'POST' }),
    onSuccess: () => settled(id),
  })
}

export function useUploadCoverMutation(id: string) {
  const settled = useEventWriteSettled()
  return useMutation({
    mutationFn: (file: File) => {
      const body = new FormData()
      body.append('file', file, file.name)
      return api(`${eventPath(id)}/cover`, eventDetailSchema, { method: 'POST', body })
    },
    onSuccess: (detail) => settled(id, detail),
  })
}

export function useSaveTicketMutation(eventId: string) {
  const settled = useEventWriteSettled()
  return useMutation({
    mutationFn: ({ ticketId, body }: { ticketId?: string; body: TicketBody }) =>
      ticketId
        ? apiVoid(`${eventPath(eventId)}/tickets/${encodeURIComponent(ticketId)}`, { method: 'PUT', body })
        : apiVoid(`${eventPath(eventId)}/tickets`, { method: 'POST', body }),
    onSuccess: () => settled(eventId),
  })
}

export function useDeactivateTicketMutation(eventId: string) {
  const settled = useEventWriteSettled()
  return useMutation({
    mutationFn: (ticketId: string) => apiVoid(`${eventPath(eventId)}/tickets/${encodeURIComponent(ticketId)}/deactivate`, { method: 'POST' }),
    onSuccess: () => settled(eventId),
  })
}

export function useSaveVoucherMutation(eventId: string) {
  const settled = useEventWriteSettled()
  return useMutation({
    mutationFn: ({ voucherId, body }: { voucherId?: string; body: VoucherBody }) =>
      voucherId
        ? apiVoid(`${eventPath(eventId)}/vouchers/${encodeURIComponent(voucherId)}`, { method: 'PUT', body })
        : apiVoid(`${eventPath(eventId)}/vouchers`, { method: 'POST', body }),
    onSuccess: () => settled(eventId),
  })
}

export function useDeactivateVoucherMutation(eventId: string) {
  const settled = useEventWriteSettled()
  return useMutation({
    mutationFn: (voucherId: string) => apiVoid(`${eventPath(eventId)}/vouchers/${encodeURIComponent(voucherId)}/deactivate`, { method: 'POST' }),
    onSuccess: () => settled(eventId),
  })
}
