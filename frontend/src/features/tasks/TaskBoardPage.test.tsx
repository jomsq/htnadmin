import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { routes } from '@/app/router'
import type { Me } from '@/app/auth/me'
import { detail } from '@/test/fixtures/events'
import { board, card } from '@/test/fixtures/tasks'
import { boardMe, goonMe, problem, server, signInAs } from '@/test/server'
import { renderRoutes } from '@/test/render'

type Json = Record<string, unknown>

const apprenticeMe: Me = {
  ...goonMe,
  id: 'u3',
  personId: 'p-ana',
  firstName: 'Ana',
  lastName: 'Bautista',
  level: 'Apprentice',
  // Resource-scoped: an apprentice may work on the tasks they are assigned to, never manage them.
  policies: ['Settings.Personal', 'Events.View', 'Tasks.ViewBoard', 'Tasks.Work'],
}

function renderBoard(path = '/events/e1/tasks') {
  const user = userEvent.setup()
  return { user, ...renderRoutes(routes, path) }
}

/** Mocks the event and the board, records every board request URL, and can change the payload. */
function mockBoard(payload: Json = board()) {
  const urls: URL[] = []
  const state = { payload }
  server.use(
    http.get('/api/events/e1', () => HttpResponse.json(detail())),
    http.get('/api/events/e1/tasks', ({ request }) => {
      urls.push(new URL(request.url))
      return HttpResponse.json(state.payload)
    }),
  )
  return { urls, state }
}

/** The same board after the server moved HTN-142 from In progress to Review. */
function movedBoard() {
  const base = board() as { columns: { status: string; count: number; cards: Json[] }[] }
  const inProgress = base.columns.find((c) => c.status === 'InProgress')
  const moved = inProgress?.cards.find((c) => c.id === 't-floorplan') ?? {}
  return {
    ...base,
    columns: base.columns.map((c) => {
      if (c.status === 'InProgress') {
        const cards = c.cards.filter((x) => x.id !== 't-floorplan')
        return { ...c, cards, count: cards.length }
      }
      if (c.status === 'Review') {
        const cards = [...c.cards, { ...moved, status: 'Review' }]
        return { ...c, cards, count: cards.length }
      }
      return c
    }),
  }
}

function column(name: string) {
  return screen.getByRole('region', { name }) as HTMLElement
}

describe('Task board', () => {
  it('shows the five columns with their counts and the legend', async () => {
    signInAs(boardMe)
    mockBoard()
    renderBoard()

    expect(await screen.findByRole('heading', { name: 'Task board' }, { timeout: 5000 })).toBeInTheDocument()
    const names = ['Backlog', 'To do', 'In progress', 'Review', 'Done']
    expect(screen.getAllByRole('region').map((s) => s.getAttribute('aria-label'))).toEqual(expect.arrayContaining(names))

    expect(within(column('Backlog')).getByText('1')).toBeInTheDocument()
    expect(within(column('In progress')).getByText('2')).toBeInTheDocument()
    expect(within(column('In progress')).getByRole('link', { name: /Finalize venue floor plan/ })).toBeInTheDocument()
    expect(within(column('Done')).getByRole('link', { name: /Book venue/ })).toBeInTheDocument()

    expect(screen.getByText(/Avatar rings: blue = Lead \(×1\.0\), teal = Collab \(×0\.7\), amber = Assistant \(×0\.3\)/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '+ New task' })).toBeInTheDocument()
    expect(within(column('Backlog')).getByRole('button', { name: /Add task/ })).toBeInTheDocument()
  })

  it('shows the due-soon chip, points, checklist count and assignee rings on a card', async () => {
    signInAs(boardMe)
    mockBoard()
    renderBoard()

    const tile = (await screen.findByRole('link', { name: /Finalize venue floor plan/ })).closest('li') as HTMLElement
    expect(within(tile).getByText('Logistics')).toBeInTheDocument()
    expect(within(tile).getByText('Due soon')).toBeInTheDocument()
    expect(within(tile).getByText('40 pts')).toBeInTheDocument()
    expect(within(tile).getByText('3/5')).toBeInTheDocument()
    // The role is in the accessible name, so colour is never the only signal.
    expect(within(tile).getByRole('img', { name: 'Alex Reyes · Lead' })).toBeInTheDocument()
    expect(within(tile).getByRole('img', { name: 'Mika Soriano · Collab' })).toBeInTheDocument()
    expect(within(tile).getByRole('img', { name: 'Ana Bautista · Assistant' })).toBeInTheDocument()
  })

  it('reads ?committee= from the URL and asks the API for that committee', async () => {
    signInAs(boardMe)
    const { urls } = mockBoard()
    renderBoard('/events/e1/tasks?committee=c-logistics')

    await screen.findByRole('heading', { name: 'Task board' }, { timeout: 5000 })
    expect(screen.getByRole('button', { name: 'Logistics' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('button', { name: 'All' })).toHaveAttribute('aria-pressed', 'false')
    await waitFor(() => expect(urls[0].searchParams.get('committeeId')).toBe('c-logistics'))
  })

  it('filters by a committee chip and puts it in the query string', async () => {
    signInAs(boardMe)
    const { urls } = mockBoard()
    const { user, router } = renderBoard()

    await screen.findByRole('heading', { name: 'Task board' }, { timeout: 5000 })
    await user.click(screen.getByRole('button', { name: 'Marketing' }))

    await waitFor(() => expect(router.state.location.search).toBe('?committee=c-marketing'))
    await waitFor(() => expect(urls.some((u) => u.searchParams.get('committeeId') === 'c-marketing')).toBe(true))
  })

  it('asks for only my tasks when the switch is on', async () => {
    signInAs(boardMe)
    const { urls } = mockBoard()
    const { user } = renderBoard()

    await screen.findByRole('heading', { name: 'Task board' }, { timeout: 5000 })
    const toggle = screen.getByRole('switch', { name: 'Only my tasks' })
    expect(toggle).toHaveAttribute('aria-checked', 'false')
    await user.click(toggle)

    expect(await screen.findByRole('switch', { name: 'Only my tasks' })).toHaveAttribute('aria-checked', 'true')
    await waitFor(() => expect(urls.some((u) => u.searchParams.get('mine') === 'true')).toBe(true))
  })

  it('moves a card from the Move to… menu and sends the new status', async () => {
    signInAs(boardMe)
    const { state } = mockBoard()
    const bodies: Json[] = []
    server.use(
      http.post('/api/tasks/t-floorplan/move', async ({ request }) => {
        bodies.push((await request.json()) as Json)
        // The server has moved it, so the next board refetch shows it in Review.
        state.payload = movedBoard()
        return HttpResponse.json({ ...card({ status: 'Review' }), eventId: 'e1' })
      }),
    )
    const { user } = renderBoard()

    await screen.findByRole('heading', { name: 'Task board' }, { timeout: 5000 })
    await user.click(within(column('In progress')).getByRole('button', { name: 'Move Finalize venue floor plan' }))
    await user.click(screen.getByRole('menuitem', { name: 'Review' }))

    await waitFor(() => expect(bodies).toEqual([{ status: 'Review' }]))
    // Optimistic update: the card sits in Review before the board is refetched.
    await waitFor(() => expect(within(column('Review')).getByRole('link', { name: /Finalize venue floor plan/ })).toBeInTheDocument())
  })

  it('rolls the card back and explains needs_approval when the server refuses', async () => {
    signInAs(boardMe)
    mockBoard()
    server.use(http.post('/api/tasks/t-floorplan/move', () => problem(409, 'Needs approval', 'needs_approval')))
    const { user } = renderBoard()

    await screen.findByRole('heading', { name: 'Task board' }, { timeout: 5000 })
    await user.click(within(column('In progress')).getByRole('button', { name: 'Move Finalize venue floor plan' }))
    await user.click(screen.getByRole('menuitem', { name: 'Done' }))

    expect((await screen.findAllByText(/Only the committee lead or an admin can finish a task/)).length).toBeGreaterThan(0)
    // Rolled back: the card is in In progress again and not in Done.
    await waitFor(() => expect(within(column('In progress')).getByRole('link', { name: /Finalize venue floor plan/ })).toBeInTheDocument())
    expect(within(column('Done')).queryByRole('link', { name: /Finalize venue floor plan/ })).not.toBeInTheDocument()
  })

  it('rolls the card back and explains needs_other_approver when the sole lead moves it to Done', async () => {
    signInAs(boardMe)
    mockBoard()
    server.use(http.post('/api/tasks/t-floorplan/move', () => problem(409, 'Needs another approver', 'needs_other_approver')))
    const { user } = renderBoard()

    await screen.findByRole('heading', { name: 'Task board' }, { timeout: 5000 })
    await user.click(within(column('In progress')).getByRole('button', { name: 'Move Finalize venue floor plan' }))
    await user.click(screen.getByRole('menuitem', { name: 'Done' }))

    expect((await screen.findAllByText(/Another committee lead or a board member has to approve this/)).length).toBeGreaterThan(0)
    // Rolled back: the card never reaches Done.
    await waitFor(() => expect(within(column('In progress')).getByRole('link', { name: /Finalize venue floor plan/ })).toBeInTheDocument())
    expect(within(column('Done')).queryByRole('link', { name: /Finalize venue floor plan/ })).not.toBeInTheDocument()
  })

  it('names the real total on a capped Done column and nowhere else', async () => {
    signInAs(boardMe)
    const base = board() as { columns: { status: string; count: number; hasMore: boolean; cards: Json[] }[] }
    mockBoard({
      ...base,
      columns: base.columns.map((c) => (c.status === 'Done' ? { ...c, count: 134, hasMore: true } : c)),
    })
    renderBoard()

    await screen.findByRole('heading', { name: 'Task board' }, { timeout: 5000 })
    expect(within(column('Done')).getByText(/Showing the newest 1 of 134 tasks\./)).toBeInTheDocument()
    expect(within(column('In progress')).queryByText(/Showing the newest/)).not.toBeInTheDocument()
    expect(within(column('Review')).queryByText(/Showing the newest/)).not.toBeInTheDocument()
  })

  it('explains no_lead when a task without a lead cannot be approved', async () => {
    signInAs(boardMe)
    mockBoard()
    server.use(http.post('/api/tasks/t-floorplan/move', () => problem(409, 'No lead', 'no_lead')))
    const { user } = renderBoard()

    await screen.findByRole('heading', { name: 'Task board' }, { timeout: 5000 })
    await user.click(within(column('In progress')).getByRole('button', { name: 'Move Finalize venue floor plan' }))
    await user.click(screen.getByRole('menuitem', { name: 'Done' }))

    expect((await screen.findAllByText(/Add a lead before approving/)).length).toBeGreaterThan(0)
  })

  it('hides the move controls on cards the user may not move', async () => {
    signInAs(apprenticeMe)
    mockBoard(board({ canCreateTask: false }))
    renderBoard()

    await screen.findByRole('heading', { name: 'Task board' }, { timeout: 5000 })
    // Their own card can be moved…
    expect(within(column('In progress')).getByRole('button', { name: 'Move Finalize venue floor plan' })).toBeInTheDocument()
    // …but a card from another committee (canMove: false) has no Move menu and no grip.
    expect(within(column('Backlog')).queryByRole('button', { name: 'Move Speaker teaser videos' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '+ New task' })).not.toBeInTheDocument()
    expect(within(column('Backlog')).queryByRole('button', { name: /\+ Add task/ })).not.toBeInTheDocument()
  })

  it('shows a loading state, then an error with retry', async () => {
    signInAs(boardMe)
    let calls = 0
    server.use(
      http.get('/api/events/e1', () => HttpResponse.json(detail())),
      http.get('/api/events/e1/tasks', () => {
        calls++
        return calls === 1 ? problem(500, 'Server error') : HttpResponse.json(board())
      }),
    )
    const { user } = renderBoard()

    expect(await screen.findByText(/Could not load the task board/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Try again' }))
    expect(await screen.findByRole('heading', { name: 'Task board' }, { timeout: 5000 })).toBeInTheDocument()
  })

  it('shows an empty state when no task matches', async () => {
    signInAs(boardMe)
    mockBoard(board({ columns: [] }))
    renderBoard()

    expect(await screen.findByText('No tasks yet')).toBeInTheDocument()
  })
})
