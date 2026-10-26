import { http, HttpResponse } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import { z } from 'zod'
import { login } from '@/features/auth/api'
import { boardMe, CSRF, problem, server } from '@/test/server'
import { api, apiRequest, apiVoid, ApiError } from './api'

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
        if (t === 't1') return HttpResponse.json({ title: 'Security token missing or invalid', status: 400, code: 'antiforgery' }, { status: 400 })
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
          { title: 'One or more validation errors occurred.', status: 400, errors: { email: ['Someone with this email is already in the organization.'] } },
          { status: 400 },
        ),
      ),
    )
    const err = await apiVoid('/api/people', { method: 'POST', body: {} }).catch((e: unknown) => e)
    expect(err).toBeInstanceOf(ApiError)
    expect((err as ApiError).status).toBe(400)
    expect((err as ApiError).fieldErrors).toEqual({ email: 'Someone with this email is already in the organization.' })
  })

  it('does not retry a 400 that is not an antiforgery failure', async () => {
    let calls = 0
    server.use(
      http.post('/api/x', () => {
        calls++
        return problem(400, 'Bad request', 'invite_invalid')
      }),
    )
    const err = await apiVoid('/api/x', { method: 'POST' }).catch((e: unknown) => e)
    expect((err as ApiError).code).toBe('invite_invalid')
    expect(calls).toBe(1)
  })
})

describe('CSRF token is bound to the user', () => {
  /** Antiforgery hands out t1, t2, ...; returns the tokens each POST /api/x carried. */
  function trackTokens() {
    let issued = 0
    const seen: Array<string | null> = []
    server.use(
      http.get('/api/auth/antiforgery', () => {
        issued++
        return HttpResponse.json({ token: `t${issued}` })
      }),
      http.post('/api/x', ({ request }) => {
        seen.push(request.headers.get('X-CSRF-TOKEN'))
        return new HttpResponse(null, { status: 204 })
      }),
    )
    return { seen, issued: () => issued }
  }

  it('refetches the token after a successful login', async () => {
    const t = trackTokens()
    let loginToken: string | null = null
    server.use(
      http.post('/api/auth/login', ({ request }) => {
        loginToken = request.headers.get('X-CSRF-TOKEN')
        return HttpResponse.json(boardMe)
      }),
    )
    await login({ email: 'alex@hackthenorth.ph', password: 'correct horse battery', rememberMe: false })
    await apiVoid('/api/x', { method: 'POST' })
    expect(loginToken).toBe('t1')
    expect(t.seen).toEqual(['t2'])
  })

  it('keeps the token after a 202 two-step challenge, then refetches after /login/2fa', async () => {
    const t = trackTokens()
    server.use(
      http.post('/api/auth/login', () => HttpResponse.json({ requires2fa: true }, { status: 202 })),
      http.post('/api/auth/login/2fa', () => HttpResponse.json(boardMe)),
    )
    const first = await login({ email: 'alex@hackthenorth.ph', password: 'correct horse battery', rememberMe: true })
    expect(first.kind).toBe('twoFactor')
    await apiRequest('/api/auth/login/2fa', { method: 'POST', body: { code: '123456', rememberMe: true } })
    expect(t.issued()).toBe(1)
    await apiVoid('/api/x', { method: 'POST' })
    expect(t.seen).toEqual(['t2'])
  })

  it.each(['/api/auth/invite/accept', '/api/auth/logout'])('refetches the token after %s', async (path) => {
    const t = trackTokens()
    server.use(http.post(path, () => new HttpResponse(null, { status: 204 })))
    await apiVoid(path, { method: 'POST', body: path.includes('invite') ? { token: 'abc', password: 'x'.repeat(12) } : undefined })
    await apiVoid('/api/x', { method: 'POST' })
    expect(t.seen).toEqual(['t2'])
  })

  it('does not refetch the token after an ordinary POST', async () => {
    const t = trackTokens()
    await apiVoid('/api/x', { method: 'POST' })
    await apiVoid('/api/x', { method: 'POST' })
    expect(t.seen).toEqual(['t1', 't1'])
  })
})
