import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { routes } from '@/app/router'
import { myTasks } from '@/test/fixtures/tasks'
import { goonMe, problem, server, signInAs } from '@/test/server'
import { renderRoutes } from '@/test/render'

function renderMyTasks(path = '/my-tasks') {
  const user = userEvent.setup()
  return { user, ...renderRoutes(routes, path) }
}

describe('My tasks', () => {
  it('groups the open tasks by event and links each card to its task dialog', async () => {
    signInAs(goonMe)
    server.use(http.get('/api/tasks/mine', () => HttpResponse.json(myTasks())))
    renderMyTasks()

    expect(await screen.findByRole('heading', { level: 1, name: 'My tasks' }, { timeout: 5000 })).toBeInTheDocument()
    expect(screen.getByText('3 open tasks assigned to you across events and committees.')).toBeInTheDocument()

    const con = screen.getByRole('region', { name: 'HTN Con 2026' })
    expect(within(con).getAllByRole('listitem')).toHaveLength(2)
    expect(within(con).getByRole('link', { name: /Finalize venue floor plan/ })).toHaveAttribute('href', '/events/e1/tasks/t-floorplan')
    expect(within(con).getByRole('link', { name: 'Open board' })).toHaveAttribute('href', '/events/e1/tasks?mine=1')

    const summit = screen.getByRole('region', { name: 'HTN Summit 2025' })
    expect(within(summit).getByRole('link', { name: /Write the post-event report/ })).toHaveAttribute('href', '/events/e2/tasks/t-report')
    // No board columns here, so no drag handles or Move menus.
    expect(screen.queryByRole('button', { name: /^Move / })).not.toBeInTheDocument()
  })

  it('shows the open count on the sidebar item', async () => {
    signInAs(goonMe)
    server.use(http.get('/api/tasks/mine', () => HttpResponse.json(myTasks())))
    renderMyTasks()

    const nav = (await screen.findAllByRole('navigation', { name: 'Main navigation' }, { timeout: 5000 }))[0]
    const link = within(nav).getByRole('link', { name: /My tasks/ })
    expect(link).toHaveAttribute('href', '/my-tasks')
    await waitFor(() => expect(link).toHaveTextContent(/My tasks\s*3\s*open tasks/))
  })

  it('shows an empty state when nothing is assigned', async () => {
    signInAs(goonMe)
    server.use(http.get('/api/tasks/mine', () => HttpResponse.json({ openCount: 0, groups: [] })))
    renderMyTasks()

    expect(await screen.findByText('No open tasks', undefined, { timeout: 5000 })).toBeInTheDocument()
  })

  it('shows an error with retry', async () => {
    signInAs(goonMe)
    let calls = 0
    server.use(
      http.get('/api/tasks/mine', () => {
        calls++
        return calls === 1 ? problem(500, 'Server error') : HttpResponse.json(myTasks())
      }),
    )
    const { user } = renderMyTasks()

    expect(await screen.findByText(/Could not load your tasks/, undefined, { timeout: 5000 })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Try again' }))
    expect(await screen.findByRole('region', { name: 'HTN Con 2026' })).toBeInTheDocument()
  })
})
