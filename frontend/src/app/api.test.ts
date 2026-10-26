import { http, HttpResponse } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import { z } from 'zod'
import { CSRF, server } from '@/test/server'
import { api, apiVoid, ApiError } from './api'

describe('api wrapper', () => {
  it('sends X-CSRF-TOKEN and credentials on POST', async () => {
    let header: string | null = null
    server.use(
      http.post('/api/people/abc/resend-invite', ({ request }) => {
        header = request.headers.get('X-CSRF-TOKEN')
        return new HttpResponse(null, { status: 204 })
      }),
    )
    const fetchSpy = vi.spyOn(globalThis, 'fetch')
    await apiVoid('/api/people/abc/resend-invite', { method: 'POST' })
    expect(header).toBe(CSRF)
    // MSW's Request drops `credentials`, so check what the wrapper passed to fetch.
    for (const [, init] of fetchSpy.mock.calls) expect(init?.credentials).toBe('include')
    expect(fetchSpy).toHaveBeenCalledTimes(2) // antiforgery + POST
    fetchSpy.mockRestore()
  })

  it('does not send the CSRF header on GET', async () => {
    let header: string | null = 'unset'
    server.use(
      http.get('/api/things', ({ request }) => {
        header = request.headers.get('X-CSRF-TOKEN')
        return HttpResponse.json({ ok: true })
      }),
    )
    await api('/api/things', z.object({ ok: z.boolean() }))
    expect(header).toBeNull()
  })

  it('fetches the antiforgery token once and reuses it', async () => {
    let tokenCalls = 0
    server.use(
      http.get('/api/auth/antiforgery', () => {
        tokenCalls++
        return HttpResponse.json({ token: CSRF })
      }),
      http.post('/api/x', () => new HttpResponse(null, { status: 204 })),
    )
    await apiVoid('/api/x', { method: 'POST' })
    await apiVoid('/api/x', { method: 'POST' })
    expect(tokenCalls).toBe(1)
  })

  it('refetches the token and retries once on an antiforgery 400', async () => {
    let tokenCalls = 0
    const seen: Array<string | null> = []
    server.use(
      http.get('/api/auth/antiforgery', () => {
        tokenCalls++
        return HttpResponse.json({ token: `t${tokenCalls}` })
      }),
      http.post('/api/x', ({ request }) => {
        const t = request.headers.get('X-CSRF-TOKEN')
        seen.push(t)
        if (t === 't1') return HttpResponse.json({ title: 'Antiforgery token validation failed', status: 400 }, { status: 400 })
        return new HttpResponse(null, { status: 204 })
      }),
    )
    await apiVoid('/api/x', { method: 'POST' })
    expect(seen).toEqual(['t1', 't2'])
  })

  it('throws ApiError with ProblemDetails field errors', async () => {
    server.use(
      http.post('/api/people', () =>
        HttpResponse.json(
          { title: 'One or more validation errors occurred.', status: 400, errors: { Email: ['Email is already in use.'] } },
          { status: 400 },
        ),
      ),
    )
    const err = await apiVoid('/api/people', { method: 'POST', body: {} }).catch((e: unknown) => e)
    expect(err).toBeInstanceOf(ApiError)
    expect((err as ApiError).status).toBe(400)
    expect((err as ApiError).fieldErrors).toEqual({ email: 'Email is already in use.' })
  })
})
