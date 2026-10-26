import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, apiVoid } from '@/app/api'
import { committeesPageSchema, type CommitteeBody, type VolunteerBody } from './schemas'

export const committeeKeys = {
  all: ['committees'] as const,
  event: (eventId: string) => ['committees', eventId] as const,
}

const eventPath = (eventId: string) => `/api/events/${encodeURIComponent(eventId)}`
const committeePath = (eventId: string, committeeId: string) => `${eventPath(eventId)}/committees/${encodeURIComponent(committeeId)}`

export function useCommitteesQuery(eventId: string | undefined) {
  return useQuery({
    queryKey: committeeKeys.event(eventId ?? ''),
    queryFn: () => api(`${eventPath(eventId ?? '')}/committees`, committeesPageSchema),
    enabled: !!eventId,
  })
}

/** Every write refetches the one list that drives the whole screen (counts, pool and flags). */
function useCommitteesRefetch(eventId: string) {
  const queryClient = useQueryClient()
  return () => queryClient.invalidateQueries({ queryKey: committeeKeys.event(eventId) })
}

export function useCreateCommitteeMutation(eventId: string) {
  const refetch = useCommitteesRefetch(eventId)
  return useMutation({
    mutationFn: (body: CommitteeBody) => apiVoid(`${eventPath(eventId)}/committees`, { method: 'POST', body }),
    onSuccess: refetch,
  })
}

export function useRenameCommitteeMutation(eventId: string) {
  const refetch = useCommitteesRefetch(eventId)
  return useMutation({
    mutationFn: ({ committeeId, body }: { committeeId: string; body: CommitteeBody }) =>
      apiVoid(committeePath(eventId, committeeId), { method: 'PUT', body }),
    onSuccess: refetch,
  })
}

export function useDeactivateCommitteeMutation(eventId: string) {
  const refetch = useCommitteesRefetch(eventId)
  return useMutation({
    mutationFn: ({ committeeId, reason }: { committeeId: string; reason: string }) =>
      apiVoid(`${committeePath(eventId, committeeId)}/deactivate`, { method: 'POST', body: { reason } }),
    onSuccess: refetch,
  })
}

/** personId null clears the lead; the previous lead stays on as a plain member. */
export function useSetLeadMutation(eventId: string) {
  const refetch = useCommitteesRefetch(eventId)
  return useMutation({
    mutationFn: ({ committeeId, personId }: { committeeId: string; personId: string | null }) =>
      apiVoid(`${committeePath(eventId, committeeId)}/lead`, { method: 'PUT', body: { personId } }),
    onSuccess: refetch,
  })
}

export function useAddMemberMutation(eventId: string) {
  const refetch = useCommitteesRefetch(eventId)
  return useMutation({
    mutationFn: ({ committeeId, personId }: { committeeId: string; personId: string }) =>
      apiVoid(`${committeePath(eventId, committeeId)}/members`, { method: 'POST', body: { personId } }),
    onSuccess: refetch,
  })
}

export function useRemoveMemberMutation(eventId: string) {
  const refetch = useCommitteesRefetch(eventId)
  return useMutation({
    mutationFn: ({ committeeId, personId }: { committeeId: string; personId: string }) =>
      apiVoid(`${committeePath(eventId, committeeId)}/members/${encodeURIComponent(personId)}`, { method: 'DELETE' }),
    onSuccess: refetch,
  })
}

export function useAttachVolunteerMutation(eventId: string) {
  const refetch = useCommitteesRefetch(eventId)
  return useMutation({
    mutationFn: ({ committeeId, volunteerId }: { committeeId: string; volunteerId: string }) =>
      apiVoid(`${committeePath(eventId, committeeId)}/volunteers/${encodeURIComponent(volunteerId)}`, { method: 'POST' }),
    onSuccess: refetch,
  })
}

export function useDetachVolunteerMutation(eventId: string) {
  const refetch = useCommitteesRefetch(eventId)
  return useMutation({
    mutationFn: ({ committeeId, volunteerId }: { committeeId: string; volunteerId: string }) =>
      apiVoid(`${committeePath(eventId, committeeId)}/volunteers/${encodeURIComponent(volunteerId)}`, { method: 'DELETE' }),
    onSuccess: refetch,
  })
}

export function useCreateVolunteerMutation(eventId: string) {
  const refetch = useCommitteesRefetch(eventId)
  return useMutation({
    mutationFn: (body: VolunteerBody) => apiVoid(`${eventPath(eventId)}/volunteers`, { method: 'POST', body }),
    onSuccess: refetch,
  })
}
