import { z } from 'zod'

/** RFC 7807 ProblemDetails as returned by the API. */
export const problemSchema = z
  .object({
    title: z.string().optional(),
    status: z.number().optional(),
    detail: z.string().optional(),
    code: z.string().optional(),
    errors: z.record(z.string(), z.array(z.string())).optional(),
  })
  .loose()

export type ProblemDetails = z.infer<typeof problemSchema>

export class ApiError extends Error {
  readonly status: number
  readonly problem: ProblemDetails

  constructor(status: number, problem: ProblemDetails) {
    super(problem.detail ?? problem.title ?? `Request failed (${status})`)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
  }

  get code(): string | undefined {
    return this.problem.code
  }

  /** Field errors keyed by camelCase field name (server may send PascalCase keys). */
  get fieldErrors(): Record<string, string> {
    const out: Record<string, string> = {}
    for (const [key, messages] of Object.entries(this.problem.errors ?? {})) {
      if (!messages.length) continue
      const field = key.length ? key.charAt(0).toLowerCase() + key.slice(1) : key
      out[field] = messages[0]
    }
    return out
  }
}

export function isApiError(e: unknown, status?: number): e is ApiError {
  return e instanceof ApiError && (status === undefined || e.status === status)
}

// ---- Cross-cutting signals (registered by AuthProvider) --------------------

type Listener = () => void
const twoFactorListeners = new Set<Listener>()
const unauthorizedListeners = new Set<Listener>()

/** Fires when the server answers 403 with code "2fa_required". */
export function onTwoFactorRequired(fn: Listener): () => void {
  twoFactorListeners.add(fn)
  return () => twoFactorListeners.delete(fn)
}

/** Fires when a non-auth request answers 401 (session expired). */
export function onUnauthorized(fn: Listener): () => void {
  unauthorizedListeners.add(fn)
  return () => unauthorizedListeners.delete(fn)
}

// ---- CSRF -------------------------------------------------------------------

const CSRF_HEADER = 'X-CSRF-TOKEN'
let csrfToken: string | null = null
let csrfPromise: Promise<string> | null = null

const antiforgerySchema = z.object({ token: z.string() })

function toUrl(path: string): string {
  // Absolute URL so fetch also works outside the browser (tests).
  return new URL(path, window.location.origin).toString()
}

/** Fetches (once) and caches the antiforgery token. */
export function getCsrfToken(force = false): Promise<string> {
  if (force) {
    csrfToken = null
    csrfPromise = null
  }
  if (csrfToken) return Promise.resolve(csrfToken)
  if (!csrfPromise) {
    csrfPromise = fetch(toUrl('/api/auth/antiforgery'), { credentials: 'include', headers: { Accept: 'application/json' } })
      .then(async (res) => {
        if (!res.ok) throw new ApiError(res.status, await readProblem(res))
        const { token } = antiforgerySchema.parse(await res.json())
        csrfToken = token
        return token
      })
      .finally(() => {
        csrfPromise = null
      })
  }
  return csrfPromise
}

/** Drop the cached token. Call after login/logout (the token is bound to the user). */
export function resetCsrfToken(): void {
  csrfToken = null
  csrfPromise = null
}

function isAntiforgeryFailure(status: number, problem: ProblemDetails): boolean {
  if (status !== 400) return false
  const text = `${problem.code ?? ''} ${problem.title ?? ''} ${problem.detail ?? ''}`
  return /antiforgery|csrf/i.test(text)
}

// ---- Core request -----------------------------------------------------------

export type HttpMethod = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE'

export type ApiOptions = {
  method?: HttpMethod
  body?: unknown
  signal?: AbortSignal
}

export type ApiResponse = { status: number; data: unknown }

async function readProblem(res: Response): Promise<ProblemDetails> {
  const text = await res.text().catch(() => '')
  if (text) {
    try {
      const parsed = problemSchema.safeParse(JSON.parse(text))
      if (parsed.success) return { status: res.status, ...parsed.data }
    } catch {
      // not JSON
    }
  }
  return { status: res.status, title: res.statusText || `Request failed (${res.status})` }
}

async function readBody(res: Response): Promise<unknown> {
  if (res.status === 204) return undefined
  const text = await res.text()
  if (!text) return undefined
  try {
    return JSON.parse(text) as unknown
  } catch {
    return text
  }
}

/**
 * Low-level request: returns status + raw JSON for any 2xx response, throws ApiError otherwise.
 * Use when the status code matters (login 200 vs 202).
 */
export async function apiRequest(path: string, options: ApiOptions = {}, retried = false): Promise<ApiResponse> {
  const method = options.method ?? 'GET'
  const headers: Record<string, string> = { Accept: 'application/json' }
  if (options.body !== undefined) headers['Content-Type'] = 'application/json'
  if (method !== 'GET') headers[CSRF_HEADER] = await getCsrfToken()

  const res = await fetch(toUrl(path), {
    method,
    headers,
    credentials: 'include',
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
    signal: options.signal,
  })

  if (res.ok) return { status: res.status, data: await readBody(res) }

  const problem = await readProblem(res)

  if (!retried && method !== 'GET' && isAntiforgeryFailure(res.status, problem)) {
    await getCsrfToken(true)
    return apiRequest(path, options, true)
  }
  if (res.status === 403 && problem.code === '2fa_required') {
    twoFactorListeners.forEach((fn) => fn())
  }
  if (res.status === 401 && !path.startsWith('/api/auth/')) {
    unauthorizedListeners.forEach((fn) => fn())
  }
  throw new ApiError(res.status, problem)
}

/** Typed request: parses the JSON body with the given zod schema. */
export async function api<S extends z.ZodType>(path: string, schema: S, options?: ApiOptions): Promise<z.infer<S>> {
  const { data } = await apiRequest(path, options)
  return schema.parse(data)
}

/** Request whose response body is ignored (204 and friends). */
export async function apiVoid(path: string, options?: ApiOptions): Promise<void> {
  await apiRequest(path, options)
}
