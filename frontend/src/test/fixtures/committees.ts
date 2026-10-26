/** API fixtures shaped like phase4-contract.md section 4A (GET /api/events/{id}/committees). */

export const person = (over: Record<string, unknown> = {}) => ({
  personId: 'p-ana',
  firstName: 'Ana',
  lastName: 'Bautista',
  level: 'Apprentice',
  joinedAt: '2026-09-01T00:00:00+08:00',
  ...over,
})

export const volunteer = (over: Record<string, unknown> = {}) => ({
  id: 'v-carla',
  name: 'Carla Diaz',
  email: null,
  mobile: null,
  ...over,
})

export const committee = (over: Record<string, unknown> = {}) => ({
  id: 'c-logistics',
  name: 'Logistics',
  sortOrder: 1,
  isActive: true,
  canManage: true,
  openTasks: 9,
  peopleCount: 4,
  lead: person({ personId: 'p-mika', firstName: 'Mika', lastName: 'Soriano', level: 'Goon' }),
  members: [person(), person({ personId: 'p-leo', firstName: 'Leo', lastName: 'Santos', level: 'Goon' })],
  volunteers: [volunteer()],
  ...over,
})

export const committeesPage = (over: Record<string, unknown> = {}) => ({
  canCreateCommittee: true,
  canManageVolunteers: true,
  committees: [
    committee(),
    committee({
      id: 'c-programs',
      name: 'Programs',
      sortOrder: 2,
      openTasks: 6,
      peopleCount: 2,
      lead: null,
      members: [person({ personId: 'p-bea', firstName: 'Bea', lastName: 'Lopez', level: 'Goon' })],
      volunteers: [],
    }),
  ],
  pool: {
    people: [
      person({ personId: 'p-ken', firstName: 'Ken', lastName: 'Morales', level: 'Goon', joinedAt: null }),
      person({ personId: 'p-celine', firstName: 'Celine', lastName: 'Villanueva', level: 'Apprentice', joinedAt: null }),
    ],
    volunteers: [volunteer({ id: 'v-pia', name: 'Pia Santos' })],
  },
  ...over,
})
