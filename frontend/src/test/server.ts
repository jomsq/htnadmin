import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import type { Me } from '@/app/auth/me'

export const CSRF = 'test-csrf-token'

export const boardMe: Me = {
  id: 'u1',
  personId: 'p1',
  firstName: 'Alex',
  lastName: 'Reyes',
  email: 'alex@hackthenorth.ph',
  level: 'Board',
  isCfo: false,
  twoFactorEnabled: true,
  // The server sends true only while the board 2FA gate applies.
  twoFactorRequired: false,
  policies: [
    'People.View',
    'People.Manage',
    'Settings.Personal',
    'Settings.Manage',
    'Finance.View',
    'Events.View',
    'Events.Manage',
    'Registrations.Manage',
    'CheckIn.Scan',
    'Committees.ManageMembers',
    'Tasks.ViewBoard',
    'Tasks.Manage',
    'Tasks.Work',
    'Leaderboard.View',
  ],
  preferences: { mode: 'system', textSize: 'default', density: 'comfortable' },
}

export const goonMe: Me = {
  ...boardMe,
  id: 'u2',
  personId: 'p2',
  firstName: 'Mika',
  lastName: 'Soriano',
  email: 'mika@hackthenorth.ph',
  level: 'Goon',
  twoFactorEnabled: false,
  twoFactorRequired: false,
  // The four task/committee policies are resource-scoped: the flat list only says "may be allowed".
  policies: [
    'People.View',
    'Settings.Personal',
    'Events.View',
    'CheckIn.Scan',
    'Committees.ManageMembers',
    'Tasks.ViewBoard',
    'Tasks.Work',
    'Leaderboard.View',
  ],
}

/** GET /api/settings/org as seeded (domain defaults, canvas brand color). */
export const orgSettings = {
  points: {
    leadMultiplier: 1,
    collabMultiplier: 0.7,
    assistantMultiplier: 0.3,
    presets: { small: 10, medium: 20, large: 40, xl: 80 },
    requireTaskApproval: true,
  },
  branding: { brandColor: '#B71C1C', logoLightUrl: null, logoDarkUrl: null, paymentDeadlineHours: 48 },
}

export const defaultHandlers = [
  http.get('/api/auth/antiforgery', () => HttpResponse.json({ token: CSRF })),
  http.get('/api/settings/org', () => HttpResponse.json(orgSettings)),
  // The sidebar "My tasks" count is fetched on every signed-in screen.
  http.get('/api/tasks/mine', () => HttpResponse.json({ groups: [], openCount: 0 })),
  http.get('/api/auth/me', () => HttpResponse.json({ title: 'Unauthorized', status: 401 }, { status: 401 })),
]

export const server = setupServer(...defaultHandlers)

/** A board account that has not turned on 2FA yet: the server gate applies. */
export const boardWithout2faMe: Me = { ...boardMe, twoFactorEnabled: false, twoFactorRequired: true }

/** ProblemDetails as the API writes it (code in the extensions). */
export function problem(status: number, title: string, code?: string, extra: Record<string, unknown> = {}) {
  return HttpResponse.json({ status, title, ...(code ? { code } : {}), ...extra }, { status })
}

/** Make /api/auth/me return this user. */
export function signInAs(me: Me) {
  server.use(http.get('/api/auth/me', () => HttpResponse.json(me)))
}
