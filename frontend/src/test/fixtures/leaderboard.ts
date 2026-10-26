/** API fixtures shaped like phase4-contract.md section 4C (leaderboard). Names follow canvas frame 14. */

type Json = Record<string, unknown>

export const leaderboardRow = (over: Json = {}) => ({
  rank: 1,
  personId: 'p2',
  name: 'Mika Soriano',
  initials: 'MS',
  level: 'Goon',
  committees: 'Logistics',
  tasksDone: 18,
  asLead: 9,
  asCollab: 6,
  asAssistant: 3,
  points: 412,
  isMe: false,
  ...over,
})

/** The eight rows of the canvas table, in order. */
export const leaderboardRows = () => [
  leaderboardRow(),
  leaderboardRow({ rank: 2, personId: 'p3', name: 'Rafael Tan', initials: 'RT', committees: 'Programs', tasksDone: 15, asLead: 7, asCollab: 6, asAssistant: 2, points: 366 }),
  leaderboardRow({ rank: 3, personId: 'p4', name: 'Jamie Lim', initials: 'JL', level: 'Apprentice', committees: 'Marketing', tasksDone: 16, asLead: 4, asCollab: 7, asAssistant: 5, points: 298 }),
  leaderboardRow({ rank: 4, personId: 'p5', name: 'Kai Cruz', initials: 'KC', committees: 'Partnerships', tasksDone: 11, asLead: 6, asCollab: 3, asAssistant: 2, points: 271 }),
  leaderboardRow({ rank: 5, personId: 'p6', name: 'Ana Bautista', initials: 'AB', level: 'Apprentice', committees: 'Logistics', tasksDone: 17, asLead: 2, asCollab: 5, asAssistant: 10, points: 240 }),
  leaderboardRow({ rank: 6, personId: 'p7', name: 'Sam Uy', initials: 'SU', committees: 'Registration & Check-in', tasksDone: 9, asLead: 5, asCollab: 2, asAssistant: 2, points: 214 }),
  leaderboardRow({ rank: 7, personId: 'p8', name: 'Gia Pascual', initials: 'GP', committees: 'Partnerships', tasksDone: 10, asLead: 3, asCollab: 5, asAssistant: 2, points: 198 }),
  leaderboardRow({ rank: 8, personId: 'p9', name: 'Marco Yu', initials: 'MY', level: 'Apprentice', committees: 'Programs', tasksDone: 12, asLead: 1, asCollab: 4, asAssistant: 7, points: 156 }),
]

export const leaderboard = (over: Json = {}) => ({
  scope: 'quarter',
  scopeLabel: 'This quarter',
  eventId: null,
  committeeId: null,
  committees: [
    { id: 'c-logistics', name: 'Logistics' },
    { id: 'c-programs', name: 'Programs' },
    { id: 'c-partnerships', name: 'Partnerships' },
    { id: 'c-marketing', name: 'Marketing' },
  ],
  events: [
    { id: 'e1', name: 'HTN Con 2026' },
    { id: 'e2', name: 'HTN Summit 2025' },
  ],
  rows: leaderboardRows(),
  me: null,
  ...over,
})

/** The caller is Mika (goonMe), far down the list: her row comes pinned instead of visible. */
export const myRow = (over: Json = {}) =>
  leaderboardRow({ rank: 14, personId: 'p2', name: 'Mika Soriano', initials: 'MS', committees: 'Logistics', tasksDone: 4, asLead: 1, asCollab: 2, asAssistant: 1, points: 62, isMe: true, ...over })
