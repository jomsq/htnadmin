import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { routes } from '@/app/router'
import { committeesPage } from '@/test/fixtures/committees'
import { detail } from '@/test/fixtures/events'
import { board, taskDetail } from '@/test/fixtures/tasks'
import { boardMe, problem, server, signInAs } from '@/test/server'
import { renderRoutes } from '@/test/render'

type Json = Record<string, unknown>

const PATH = '/events/e1/tasks/t-floorplan'

function mockTask(task: Json = taskDetail()) {
  const state = { task }
  server.use(
    http.get('/api/events/e1', () => HttpResponse.json(detail())),
    http.get('/api/events/e1/tasks', () => HttpResponse.json(board())),
    http.get('/api/events/e1/committees', () => HttpResponse.json(committeesPage())),
    http.get('/api/tasks/t-floorplan', () => HttpResponse.json(state.task)),
  )
  return state
}

function renderDialog(path = PATH) {
  const user = userEvent.setup()
  return { user, ...renderRoutes(routes, path) }
}

function dialog() {
  return screen.getByRole('dialog') as HTMLElement
}

/** Waits for the loaded task (the dialog shows a skeleton while the detail is fetched). */
async function openDialog() {
  // The board and the dialog are lazy routes, so the first render can take a moment.
  await screen.findByRole('heading', { name: 'Finalize venue floor plan' }, { timeout: 5000 })
  return dialog()
}

describe('Task detail dialog', () => {
  it('deep-links over the board and shows the header, checklist, attachments and activity', async () => {
    signInAs(boardMe)
    mockTask()
    renderDialog()

    const panel = await openDialog()
    expect(within(panel).getByText('HTN Con 2026 · Logistics · HTN-142')).toBeInTheDocument()
    expect(within(panel).getByRole('heading', { name: 'Finalize venue floor plan' })).toBeInTheDocument()
    expect(within(panel).getByRole('button', { name: 'Close' })).toBeInTheDocument()
    expect(within(panel).getByLabelText('Status')).toHaveValue('InProgress')
    expect(within(panel).getByText('Due Nov 2, 2026')).toBeInTheDocument()
    expect(within(panel).getByText('Venue')).toBeInTheDocument()

    // Checklist with its progress bar.
    expect(within(panel).getByText('3 of 5')).toBeInTheDocument()
    expect(within(panel).getByRole('progressbar', { name: '3 of 5 checklist items done' })).toHaveAttribute('aria-valuenow', '60')
    expect(within(panel).getByText('Get SMX CAD file')).toBeInTheDocument()

    // Attachment with type, size and a download link.
    expect(within(panel).getByRole('link', { name: /Download floorplan-v3\.pdf \(PDF, 121 KB\)/ })).toHaveAttribute(
      'href',
      '/api/tasks/t-floorplan/attachments/f-1',
    )

    // Activity log.
    expect(within(panel).getByText('set base points to 40')).toBeInTheDocument()
    expect(within(panel).getByText('uploaded floorplan-v3.pdf')).toBeInTheDocument()

    // The board is still behind the dialog.
    expect(screen.getByRole('region', { name: 'In progress' })).toBeInTheDocument()
  })

  it('shows the base points stepper and the computed points per role', async () => {
    signInAs(boardMe)
    mockTask()
    renderDialog()

    const panel = await openDialog()
    const aside = within(panel).getByRole('complementary', { name: 'Points and approval' })
    expect(within(aside).getByRole('button', { name: 'Decrease base points' })).toBeInTheDocument()
    expect(within(aside).getByRole('spinbutton')).toHaveValue(40)
    expect(within(aside).getByText('Admin & lead only')).toBeInTheDocument()

    expect(within(aside).getByText('Lead · × 1.0')).toBeInTheDocument()
    expect(within(aside).getByText('40 pts')).toBeInTheDocument()
    expect(within(aside).getByText('Collab · × 0.7')).toBeInTheDocument()
    expect(within(aside).getByText('28 pts')).toBeInTheDocument()
    expect(within(aside).getByText('Assistant · × 0.3')).toBeInTheDocument()
    expect(within(aside).getByText('12 pts')).toBeInTheDocument()
  })

  it('hides the base points stepper, the approve button and the editing actions without canEdit', async () => {
    signInAs(boardMe)
    mockTask(taskDetail({ status: 'Review', canEdit: false, canApprove: false, canWork: true }))
    renderDialog()

    const panel = await openDialog()
    expect(within(panel).queryByRole('button', { name: 'Decrease base points' })).not.toBeInTheDocument()
    expect(within(panel).queryByRole('button', { name: /Approve & mark done/ })).not.toBeInTheDocument()
    expect(within(panel).queryByRole('button', { name: /Add lead, collab or assistant/ })).not.toBeInTheDocument()
    // The rule is spelled out instead of a hidden button.
    expect(within(panel).getByText('Only the committee lead or an admin can finish a task.')).toBeInTheDocument()
  })

  it('approves a task in Review', async () => {
    signInAs(boardMe)
    const state = mockTask(taskDetail({ status: 'Review' }))
    let approved = 0
    server.use(
      http.post('/api/tasks/t-floorplan/approve', () => {
        approved++
        state.task = taskDetail({
          status: 'Done',
          approvedAt: '2026-10-26T10:00:00+08:00',
          approvedByName: 'Alex Reyes',
          canEdit: false,
          canApprove: false,
          canReopen: true,
          canArchive: false,
        })
        return HttpResponse.json(state.task)
      }),
    )
    const { user } = renderDialog()

    await openDialog()
    await user.click(screen.getByRole('button', { name: 'Approve & mark done' }))

    await waitFor(() => expect(approved).toBe(1))
    expect(await screen.findByRole('button', { name: 'Reopen' })).toBeInTheDocument()
  })

  it('disables Approve and explains why when the approver is the only lead', async () => {
    signInAs(boardMe)
    mockTask(taskDetail({ status: 'Review', canApprove: false, needsOtherApprover: true }))
    let approved = 0
    server.use(
      http.post('/api/tasks/t-floorplan/approve', () => {
        approved++
        return HttpResponse.json(taskDetail({ status: 'Done' }))
      }),
    )
    const { user } = renderDialog()

    const panel = await openDialog()
    const approve = within(panel).getByRole('button', { name: 'Approve & mark done' })
    expect(approve).toBeDisabled()
    // The reason is on the page next to the button, not only in a tooltip.
    const noteId = approve.getAttribute('aria-describedby') ?? ''
    expect(within(panel).getByText('Another committee lead or a board member has to approve this.')).toBeInTheDocument()
    // The sentence is wired to the button, so screen readers get the reason with it.
    expect(document.getElementById(noteId)).toHaveTextContent('Another committee lead or a board member has to approve this.')
    // The generic "only the lead or an admin" line would be wrong here.
    expect(within(panel).queryByText('Only the committee lead or an admin can finish a task.')).not.toBeInTheDocument()

    await user.click(approve)
    expect(approved).toBe(0)
  })

  it('offers only Reopen on a done task with live awards, and Archive once it is reopened', async () => {
    signInAs(boardMe)
    // canArchive is false while the awards are live: archiving would leave the points on the leaderboard.
    const state = mockTask(
      taskDetail({
        status: 'Done',
        approvedAt: '2026-10-20T10:00:00+08:00',
        approvedByName: 'Mika Soriano',
        canEdit: false,
        canApprove: false,
        canReopen: true,
        canArchive: false,
      }),
    )
    server.use(
      http.post('/api/tasks/t-floorplan/reopen', () => {
        state.task = taskDetail({ status: 'Review', canReopen: false, canArchive: true })
        return HttpResponse.json(state.task)
      }),
    )
    const { user } = renderDialog()

    const panel = await openDialog()
    expect(within(panel).getByRole('button', { name: 'Reopen' })).toBeInTheDocument()
    expect(within(panel).queryByRole('button', { name: 'Archive task' })).not.toBeInTheDocument()
    expect(within(panel).queryByRole('button', { name: /Approve & mark done/ })).not.toBeInTheDocument()

    await user.click(within(panel).getByRole('button', { name: 'Reopen' }))
    const confirm = await screen.findByRole('alertdialog', { name: /Reopen HTN-142/ })
    await user.type(within(confirm).getByLabelText('Reason'), 'Approved by mistake.')
    await user.click(within(confirm).getByRole('button', { name: 'Reopen task' }))

    // The awards are reversed, so the card can now be archived.
    expect(await screen.findByRole('button', { name: 'Archive task' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Reopen' })).not.toBeInTheDocument()
  })

  it('hides Reopen, Archive and Restore from someone who only works on the task', async () => {
    signInAs(boardMe)
    mockTask(
      taskDetail({
        status: 'Done',
        approvedAt: '2026-10-20T10:00:00+08:00',
        approvedByName: 'Mika Soriano',
        canEdit: false,
        canWork: false,
        canApprove: false,
        canReopen: false,
        canArchive: false,
        canRestore: false,
      }),
    )
    renderDialog()

    const panel = await openDialog()
    expect(within(panel).queryByRole('button', { name: 'Reopen' })).not.toBeInTheDocument()
    expect(within(panel).queryByRole('button', { name: 'Archive task' })).not.toBeInTheDocument()
    expect(within(panel).queryByRole('button', { name: /Restore/ })).not.toBeInTheDocument()
  })

  it('reads as archived and restores the task to the board', async () => {
    signInAs(boardMe)
    const state = mockTask(
      taskDetail({
        status: 'ToDo',
        isArchived: true,
        archiveReason: 'Duplicate of HTN-180.',
        canEdit: false,
        canWork: false,
        canApprove: false,
        canComment: false,
        canReopen: false,
        canArchive: false,
        canRestore: true,
      }),
    )
    let restored = 0
    server.use(
      http.post('/api/tasks/t-floorplan/restore', () => {
        restored++
        state.task = taskDetail({ status: 'ToDo' })
        return HttpResponse.json(state.task)
      }),
    )
    const { user } = renderDialog()

    const panel = await openDialog()
    expect(within(panel).getByText('Archived')).toBeInTheDocument()
    expect(within(panel).getByText(/This task is archived, so it is off the board and read only\./)).toBeInTheDocument()
    expect(within(panel).getByText(/Reason: Duplicate of HTN-180\./)).toBeInTheDocument()
    expect(within(panel).queryByRole('button', { name: 'Archive task' })).not.toBeInTheDocument()
    expect(within(panel).queryByLabelText('Write a comment')).not.toBeInTheDocument()

    await user.click(within(panel).getByRole('button', { name: 'Restore to the board' }))

    await waitFor(() => expect(restored).toBe(1))
    expect(await screen.findByRole('button', { name: 'Archive task' })).toBeInTheDocument()
    expect(screen.queryByText(/This task is archived/)).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Restore to the board' })).not.toBeInTheDocument()
  })

  it('shows a refused mention on the comment field instead of a bare toast', async () => {
    signInAs(boardMe)
    mockTask()
    server.use(
      http.post('/api/tasks/t-floorplan/comments', () =>
        problem(400, 'One or more validation errors occurred.', undefined, {
          errors: { mentionPersonIds: ['Choose people from the list.'] },
        }),
      ),
    )
    const { user } = renderDialog()

    await openDialog()
    const box = within(dialog()).getByLabelText('Write a comment')
    await user.type(box, 'Please check exit B @Mika')
    const option = await screen.findByRole('option', { name: 'Mika Soriano' })
    await user.click(within(option).getByRole('button', { name: 'Mika Soriano' }))
    await user.click(within(dialog()).getByRole('button', { name: 'Comment' }))

    const error = await within(dialog()).findByText(/Choose people from the list\./)
    expect(error).toHaveAttribute('role', 'alert')
    expect(box).toHaveAttribute('aria-invalid', 'true')
    expect(box.getAttribute('aria-describedby')).toContain(error.id)
    // The comment is kept so the mention can be fixed.
    expect(box).toHaveValue('Please check exit B @Mika Soriano ')
  })

  it('requires a reason to reopen a done task', async () => {
    signInAs(boardMe)
    mockTask(
      taskDetail({
        status: 'Done',
        approvedAt: '2026-10-20T10:00:00+08:00',
        approvedByName: 'Mika Soriano',
        canEdit: false,
        canApprove: false,
        canReopen: true,
        canArchive: false,
      }),
    )
    const bodies: Json[] = []
    server.use(
      http.post('/api/tasks/t-floorplan/reopen', async ({ request }) => {
        bodies.push((await request.json()) as Json)
        return HttpResponse.json(taskDetail({ status: 'Review' }))
      }),
    )
    const { user } = renderDialog()

    await openDialog()
    await user.click(screen.getByRole('button', { name: 'Reopen' }))
    const confirm = await screen.findByRole('alertdialog', { name: /Reopen HTN-142/ })

    await user.click(within(confirm).getByRole('button', { name: 'Reopen task' }))
    expect(await within(confirm).findByText('Enter a reason.')).toBeInTheDocument()
    expect(bodies).toHaveLength(0)

    await user.type(within(confirm).getByLabelText('Reason'), 'Floor plan changed after the fire marshal visit.')
    await user.click(within(confirm).getByRole('button', { name: 'Reopen task' }))

    await waitFor(() => expect(bodies).toEqual([{ reason: 'Floor plan changed after the fire marshal visit.', status: 'Review' }]))
  })

  it('forces Assistant and zero points when the new assignee is a volunteer', async () => {
    signInAs(boardMe)
    mockTask()
    const bodies: Json[] = []
    server.use(
      http.post('/api/tasks/t-floorplan/assignees', async ({ request }) => {
        bodies.push((await request.json()) as Json)
        return HttpResponse.json(taskDetail())
      }),
    )
    const { user } = renderDialog()

    await openDialog()
    await user.click(screen.getByRole('button', { name: '+ Add lead, collab or assistant' }))
    const add = await screen.findByRole('dialog', { name: 'Add lead, collab or assistant' })

    await user.selectOptions(within(add).getByLabelText('Role'), 'Lead')
    await user.selectOptions(within(add).getByLabelText('Person or volunteer'), 'volunteer:v-carla')

    const role = within(add).getByLabelText('Role')
    await waitFor(() => expect(role).toHaveValue('Assistant'))
    expect(role).toBeDisabled()
    expect(within(add).getByText('Volunteers help as Assistant and never earn points.')).toBeInTheDocument()
    expect(within(add).getByText('0 pts')).toBeInTheDocument()
    expect(within(add).getByText('Volunteer')).toBeInTheDocument()

    await user.click(within(add).getByRole('button', { name: 'Add to task' }))
    await waitFor(() => expect(bodies).toEqual([{ volunteerId: 'v-carla', role: 'Assistant' }]))
  })

  it('sends mentionPersonIds for an @mention in a comment', async () => {
    signInAs(boardMe)
    mockTask()
    const bodies: Json[] = []
    server.use(
      http.post('/api/tasks/t-floorplan/comments', async ({ request }) => {
        bodies.push((await request.json()) as Json)
        return HttpResponse.json(taskDetail())
      }),
    )
    const { user } = renderDialog()

    await openDialog()
    const box = within(dialog()).getByLabelText('Write a comment')
    await user.type(box, 'Can you check exit B @Mika')

    const option = await screen.findByRole('option', { name: 'Mika Soriano' })
    await user.click(within(option).getByRole('button', { name: 'Mika Soriano' }))
    expect(box).toHaveValue('Can you check exit B @Mika Soriano ')

    await user.click(within(dialog()).getByRole('button', { name: 'Comment' }))
    await waitFor(() =>
      expect(bodies).toEqual([{ body: 'Can you check exit B @Mika Soriano', mentionPersonIds: ['p-mika'] }]),
    )
  })

  it('rejects an .exe attachment and a file over 5 MB before uploading', async () => {
    signInAs(boardMe)
    mockTask()
    let uploads = 0
    server.use(
      http.post('/api/tasks/t-floorplan/attachments', () => {
        uploads++
        return HttpResponse.json(taskDetail())
      }),
    )
    const { container } = renderDialog()

    await openDialog()
    const input = container.ownerDocument.querySelector('input[type="file"]') as HTMLInputElement

    fireEvent.change(input, { target: { files: [new File(['MZ'], 'payload.exe', { type: 'application/x-msdownload' })] } })
    expect(await screen.findByText('Attach a PNG, JPG or PDF file.')).toBeInTheDocument()

    const big = new File(['x'], 'huge.pdf', { type: 'application/pdf' })
    Object.defineProperty(big, 'size', { value: 6 * 1024 * 1024 })
    fireEvent.change(input, { target: { files: [big] } })
    expect(await screen.findByText('This file is larger than 5 MB. Attach a smaller file.')).toBeInTheDocument()

    expect(uploads).toBe(0)
  })

  it('shows the server message when the task cannot be loaded', async () => {
    signInAs(boardMe)
    server.use(
      http.get('/api/events/e1', () => HttpResponse.json(detail())),
      http.get('/api/events/e1/tasks', () => HttpResponse.json(board())),
      http.get('/api/tasks/t-floorplan', () => problem(404, 'Not found')),
    )
    renderDialog()

    expect(await screen.findByText(/This task was archived or the link is wrong/, undefined, { timeout: 5000 })).toBeInTheDocument()
  })
})
