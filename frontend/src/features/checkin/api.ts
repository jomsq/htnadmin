import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/app/api'
import {
  checkInStatsSchema,
  recentScansSchema,
  scanResponseSchema,
  searchResultsSchema,
  type ManualBody,
  type ScanBody,
} from './schemas'

/** Stats and recent scans refresh this often, and right after every scan. */
export const CHECKIN_REFRESH_MS = 15_000

export const checkInKeys = {
  all: (eventId: string) => ['checkin', eventId] as const,
  stats: (eventId: string, station: string) => ['checkin', eventId, 'stats', { station }] as const,
  recent: (eventId: string, station: string) => ['checkin', eventId, 'recent', { station }] as const,
  search: (eventId: string, q: string) => ['checkin', eventId, 'search', { q }] as const,
}

const base = (eventId: string) => `/api/events/${encodeURIComponent(eventId)}/check-in`

export function useCheckInStatsQuery(eventId: string, station: string, enabled = true) {
  return useQuery({
    queryKey: checkInKeys.stats(eventId, station),
    queryFn: () => api(`${base(eventId)}/stats?station=${encodeURIComponent(station)}`, checkInStatsSchema),
    enabled: enabled && !!eventId,
    refetchInterval: CHECKIN_REFRESH_MS,
    staleTime: 0,
  })
}

export function useRecentScansQuery(eventId: string, station: string, enabled = true) {
  return useQuery({
    queryKey: checkInKeys.recent(eventId, station),
    queryFn: () => api(`${base(eventId)}/recent?station=${encodeURIComponent(station)}`, recentScansSchema),
    enabled: enabled && !!eventId,
    refetchInterval: CHECKIN_REFRESH_MS,
    staleTime: 0,
  })
}

/** Search needs at least 2 characters; the server caps results at 20. */
export function useCheckInSearchQuery(eventId: string, q: string) {
  const term = q.trim()
  return useQuery({
    queryKey: checkInKeys.search(eventId, term),
    queryFn: () => api(`${base(eventId)}/search?q=${encodeURIComponent(term)}`, searchResultsSchema),
    enabled: !!eventId && term.length >= 2,
    placeholderData: keepPreviousData,
    staleTime: 0,
  })
}

/** After any scan the counts and recent list change; search rows may change status too. */
function useRefreshAfterScan(eventId: string) {
  const queryClient = useQueryClient()
  return () => queryClient.invalidateQueries({ queryKey: checkInKeys.all(eventId) })
}

export function useScanMutation(eventId: string) {
  const refresh = useRefreshAfterScan(eventId)
  return useMutation({
    mutationFn: (body: ScanBody) => api(`${base(eventId)}/scan`, scanResponseSchema, { method: 'POST', body }),
    onSettled: () => {
      void refresh()
    },
  })
}

export function useManualCheckInMutation(eventId: string) {
  const refresh = useRefreshAfterScan(eventId)
  return useMutation({
    mutationFn: (body: ManualBody) => api(`${base(eventId)}/manual`, scanResponseSchema, { method: 'POST', body }),
    onSettled: () => {
      void refresh()
    },
  })
}
