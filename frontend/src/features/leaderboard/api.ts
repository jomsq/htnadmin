import { useQuery } from '@tanstack/react-query'
import { api } from '@/app/api'
import { leaderboardSchema, type LeaderboardScope } from './schemas'

export type LeaderboardParams = {
  scope: LeaderboardScope
  /** Required by the server when scope is "event" (400 otherwise). */
  eventId?: string
  committeeId?: string
}

export const leaderboardKeys = {
  all: ['leaderboard'] as const,
  view: (params: LeaderboardParams) =>
    ['leaderboard', { scope: params.scope, eventId: params.eventId ?? null, committeeId: params.committeeId ?? null }] as const,
}

function leaderboardUrl(params: LeaderboardParams): string {
  const search = new URLSearchParams({ scope: params.scope })
  if (params.eventId) search.set('eventId', params.eventId)
  if (params.committeeId) search.set('committeeId', params.committeeId)
  return `/api/leaderboard?${search.toString()}`
}

/** GET /api/leaderboard (policy Leaderboard.View; the server enforces it). */
export function useLeaderboardQuery(params: LeaderboardParams, enabled = true) {
  return useQuery({
    queryKey: leaderboardKeys.view(params),
    queryFn: () => api(leaderboardUrl(params), leaderboardSchema),
    enabled,
  })
}
