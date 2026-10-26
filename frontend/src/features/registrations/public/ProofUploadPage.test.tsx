import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderRoutes } from '@/test/render'
import { problem, server } from '@/test/server'
import { ProofUploadPage } from './ProofUploadPage'

const info = (over: Record<string, unknown> = {}) => ({
  eventName: 'HTN Con 2026',
  participantId: 'HTN-7K3Q-9XWD',
  amountDue: 1500,
  deadline: '2026-10-27T09:00:00Z',
  uploaded: false,
  ...over,
})

function renderPage() {
  return renderRoutes([{ path: '/pay/:token', element: <ProofUploadPage /> }], '/pay/tok123')
}

function fileInput(): HTMLInputElement {
  const input = document.querySelector<HTMLInputElement>('input[type="file"]')
  if (!input) throw new Error('no file input')
  return input
}

/** A file of `size` bytes without allocating it (jsdom reads `size` from the parts). */
function fileOf(name: string, type: string, size: number): File {
  const f = new File(['x'], name, { type })
  Object.defineProperty(f, 'size', { value: size })
  return f
}

describe('Proof of payment upload', () => {
  it('shows the event, participant ID and amount, and rejects the wrong type or size', async () => {
    server.use(http.get('/api/public/proof/tok123', () => HttpResponse.json(info())))
    const user = userEvent.setup({ applyAccept: false })
    renderPage()
    expect(await screen.findByRole('heading', { level: 1, name: 'HTN Con 2026' })).toBeInTheDocument()
    expect(screen.getByText('HTN-7K3Q-9XWD')).toHaveClass('font-mono')
    expect(screen.getByText('₱1,500')).toBeInTheDocument()

    await user.upload(fileInput(), fileOf('receipt.pdf', 'application/pdf', 1000))
    expect(await screen.findByText('Upload a PNG or JPG image.')).toBeInTheDocument()

    await user.upload(fileInput(), fileOf('receipt.jpg', 'image/jpeg', 5 * 1024 * 1024 + 1))
    expect(await screen.findByText(/The image is larger than 5 MB/)).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Upload proof of payment' }))
    expect(await screen.findByText('Choose a screenshot or photo of your receipt.')).toBeInTheDocument()
    expect(screen.getByText('Enter the reference number from your receipt.')).toBeInTheDocument()
  })

  it('uploads the image with the reference and shows the success state', async () => {
    let uploaded = false
    let reference: FormDataEntryValue | null = null
    let hasFile = false
    server.use(
      http.get('/api/public/proof/tok123', () => HttpResponse.json(info({ uploaded }))),
      http.post('/api/public/proof/tok123', async ({ request }) => {
        const form = await request.formData()
        reference = form.get('reference')
        hasFile = form.get('file') !== null
        uploaded = true
        return new HttpResponse(null, { status: 204 })
      }),
    )
    const user = userEvent.setup({ applyAccept: false })
    renderPage()
    await screen.findByRole('button', { name: 'Choose image' })
    await user.upload(fileInput(), fileOf('gcash.png', 'image/png', 200_000))
    expect(screen.getByText('gcash.png')).toBeInTheDocument()
    await user.type(screen.getByLabelText('Reference number'), '7741 0923')
    await user.click(screen.getByRole('button', { name: 'Upload proof of payment' }))
    expect(await screen.findByRole('heading', { name: 'Proof received' })).toBeInTheDocument()
    expect(reference).toBe('7741 0923')
    expect(hasFile).toBe(true)

    await user.click(screen.getByRole('button', { name: 'Upload a different file' }))
    expect(await screen.findByRole('button', { name: 'Replace proof of payment' })).toBeInTheDocument()
  })

  it('lets a registrant replace an already uploaded proof', async () => {
    server.use(http.get('/api/public/proof/tok123', () => HttpResponse.json(info({ uploaded: true }))))
    const user = userEvent.setup()
    renderPage()
    expect(await screen.findByText(/We already have your proof of payment/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Replace the uploaded proof' }))
    expect(screen.getByRole('heading', { name: 'Replace your proof of payment' })).toBeInTheDocument()
  })

  it('explains an invalid or expired link', async () => {
    server.use(http.get('/api/public/proof/tok123', () => problem(404, 'Not Found')))
    renderPage()
    expect(await screen.findByText('Link not valid')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Upload proof of payment' })).not.toBeInTheDocument()
  })
})
