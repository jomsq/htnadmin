/** API fixtures shaped like phase4-contract.md section 4B (task board, task detail, my tasks). */

type Json = Record<string, unknown>

export const assignee = (over: Json = {}) => ({ name: 'Alex Reyes', initials: 'AR', role: 'Lead', isVolunteer: false, ...over })

export const card = (over: Json = {}) => ({
  id: 't-floorplan',
  reference: 'HTN-142',
  title: 'Finalize venue floor plan',
  committeeId: 'c-logistics',
  committeeName: 'Logistics',
  status: 'InProgress',
  dueAt: '2026-11-02T17:00:00+08:00',
  dueSoon: true,
  overdue: false,
  checklistDone: 3,
  checklistTotal: 5,
  basePoints: 40,
  labels: ['Venue'],
  sortOrder: 100,
  canMove: true,
  isMine: true,
  assignees: [
    assignee(),
    assignee({ name: 'Mika Soriano', initials: 'MS', role: 'Collab' }),
    assignee({ name: 'Ana Bautista', initials: 'AB', role: 'Assistant' }),
  ],
  ...over,
})

export const board = (over: Json = {}) => ({
  canCreateTask: true,
  committees: [
    { id: 'c-logistics', name: 'Logistics', canManage: true },
    { id: 'c-marketing', name: 'Marketing', canManage: false },
  ],
  columns: [
    {
      status: 'Backlog',
      count: 1,
      hasMore: false,
      cards: [
        card({
          id: 't-teaser',
          reference: 'HTN-150',
          title: 'Speaker teaser videos',
          committeeId: 'c-marketing',
          committeeName: 'Marketing',
          status: 'Backlog',
          dueSoon: false,
          checklistDone: 0,
          checklistTotal: 4,
          basePoints: 30,
          canMove: false,
          isMine: false,
          assignees: [assignee({ name: 'Jess Lim', initials: 'JL' })],
        }),
      ],
    },
    {
      status: 'ToDo',
      count: 1,
      hasMore: false,
      cards: [
        card({
          id: 't-lanyards',
          reference: 'HTN-144',
          title: 'Print ID lanyards',
          status: 'ToDo',
          dueSoon: false,
          checklistDone: 0,
          checklistTotal: 2,
          basePoints: 20,
          assignees: [assignee({ name: 'Leo Santos', initials: 'LS' })],
        }),
      ],
    },
    { status: 'InProgress', count: 2, hasMore: false, cards: [card(), card({ id: 't-social', reference: 'HTN-151', title: 'Social posts: week 3', committeeId: 'c-marketing', committeeName: 'Marketing', status: 'InProgress', dueSoon: false, basePoints: 15, canMove: false, isMine: false })] },
    {
      status: 'Review',
      count: 1,
      hasMore: false,
      cards: [
        card({
          id: 't-desk',
          reference: 'HTN-148',
          title: 'Check-in desk staffing plan',
          committeeId: 'c-registration',
          committeeName: 'Registration & Check-in',
          status: 'Review',
          dueSoon: false,
          checklistDone: 4,
          checklistTotal: 4,
          basePoints: 20,
        }),
      ],
    },
    {
      status: 'Done',
      count: 1,
      hasMore: false,
      cards: [
        card({
          id: 't-venue',
          reference: 'HTN-101',
          title: 'Book venue',
          status: 'Done',
          dueSoon: false,
          checklistDone: 3,
          checklistTotal: 3,
          basePoints: 50,
          canMove: false,
        }),
      ],
    },
  ],
  ...over,
})

export const taskDetail = (over: Json = {}) => ({
  id: 't-floorplan',
  reference: 'HTN-142',
  eventId: 'e1',
  eventName: 'HTN Con 2026',
  committeeId: 'c-logistics',
  committeeName: 'Logistics',
  title: 'Finalize venue floor plan',
  description: 'Lay out registration desks, sponsor booths and the main hall seating.',
  status: 'InProgress',
  basePoints: 40,
  dueAt: '2026-11-02T17:00:00+08:00',
  labels: ['Venue'],
  isArchived: false,
  createdAt: '2026-10-18T16:12:00+08:00',
  createdByName: 'Mika Soriano',
  assignedByName: 'Mika Soriano (lead)',
  approvedAt: null,
  approvedByName: null,
  requireTaskApproval: true,
  multipliers: { lead: 1, collab: 0.7, assistant: 0.3 },
  presets: { small: 10, medium: 20, large: 40, xl: 80 },
  canEdit: true,
  canWork: true,
  canApprove: true,
  canComment: true,
  needsOtherApprover: false,
  assignees: [
    { id: 'a-1', personId: 'p-alex', volunteerId: null, name: 'Alex Reyes', initials: 'AR', role: 'Lead', multiplier: 1, points: 40, isVolunteer: false },
    { id: 'a-2', personId: 'p-mika', volunteerId: null, name: 'Mika Soriano', initials: 'MS', role: 'Collab', multiplier: 0.7, points: 28, isVolunteer: false },
    { id: 'a-3', personId: 'p-ana', volunteerId: null, name: 'Ana Bautista', initials: 'AB', role: 'Assistant', multiplier: 0.3, points: 12, isVolunteer: false },
  ],
  checklist: [
    { id: 'k-1', text: 'Get SMX CAD file', isDone: true, sortOrder: 1 },
    { id: 'k-2', text: 'Place registration desks', isDone: true, sortOrder: 2 },
    { id: 'k-3', text: 'Place sponsor booths (12)', isDone: true, sortOrder: 3 },
    { id: 'k-4', text: 'Mark power points & exits', isDone: false, sortOrder: 4 },
    { id: 'k-5', text: 'Share final PDF with Partnerships', isDone: false, sortOrder: 5 },
  ],
  attachments: [
    {
      id: 'f-1',
      fileName: 'floorplan-v3.pdf',
      contentType: 'application/pdf',
      sizeBytes: 123456,
      uploadedAt: '2026-10-24T10:03:00+08:00',
      uploadedByName: 'Ana Bautista',
      url: '/api/tasks/t-floorplan/attachments/f-1',
    },
  ],
  comments: [
    {
      id: 'cm-1',
      authorName: 'Alex Reyes',
      authorInitials: 'AR',
      body: 'Booth 7 blocks exit B, can we move it?',
      mentions: [],
      createdAt: '2026-10-25T09:40:00+08:00',
      editedAt: null,
      isDeleted: false,
      canEdit: true,
    },
  ],
  activity: [
    { at: '2026-10-18T16:12:00+08:00', actorName: 'Mika Soriano', actorInitials: 'MS', kind: 'points_changed', text: 'set base points to 40' },
    { at: '2026-10-24T10:03:00+08:00', actorName: 'Ana Bautista', actorInitials: 'AB', kind: 'attachment_added', text: 'uploaded floorplan-v3.pdf' },
    {
      at: '2026-10-25T09:40:00+08:00',
      actorName: 'Alex Reyes',
      actorInitials: 'AR',
      kind: 'comment_added',
      text: 'commented: “Booth 7 blocks exit B, can we move it?”',
    },
  ],
  awards: [],
  ...over,
})

export const myTasks = (over: Json = {}) => ({
  openCount: 3,
  groups: [
    {
      eventId: 'e1',
      eventName: 'HTN Con 2026',
      tasks: [card(), card({ id: 't-lanyards', reference: 'HTN-144', title: 'Print ID lanyards', status: 'ToDo', dueSoon: false })],
    },
    {
      eventId: 'e2',
      eventName: 'HTN Summit 2025',
      tasks: [
        card({
          id: 't-report',
          reference: 'HTS-012',
          title: 'Write the post-event report',
          committeeId: 'c-programs',
          committeeName: 'Programs',
          status: 'Review',
          dueSoon: false,
          basePoints: 25,
        }),
      ],
    },
  ],
  ...over,
})
