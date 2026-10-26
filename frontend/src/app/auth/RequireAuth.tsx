import type { ReactNode } from 'react'
import { Navigate, Outlet, useLocation, useSearchParams } from 'react-router'
import { safeNext } from '@/lib/format'
import { useAuth } from './auth-context'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { PageSpinner } from '@/components/ui/Spinner'

/**
 * Route guard: signed-out users go to /login?next=<path>.
 * Users who must turn on 2-step verification go to /setup-2fa (unless allowTwoFactorSetup).
 */
export function RequireAuth({ allowTwoFactorSetup = false, children }: { allowTwoFactorSetup?: boolean; children?: ReactNode }) {
  const { me, status, needsTwoFactorSetup, retry } = useAuth()
  const location = useLocation()

  if (status === 'loading') return <PageSpinner />
  if (status === 'error') {
    return (
      <div className="mx-auto max-w-md p-6">
        <Alert tone="red" live="alert" action={<Button variant="secondary" size="sm" onClick={retry}>Try again</Button>}>
          We could not reach HTN Admin. Check your connection and try again.
        </Alert>
      </div>
    )
  }
  if (!me && !needsTwoFactorSetup) {
    const next = `${location.pathname}${location.search}`
    const to = next === '/' ? '/login' : `/login?next=${encodeURIComponent(next)}`
    return <Navigate to={to} replace />
  }
  if (needsTwoFactorSetup && !allowTwoFactorSetup) {
    return <Navigate to="/setup-2fa" replace state={{ next: `${location.pathname}${location.search}` }} />
  }
  return children ?? <Outlet />
}

/** For /login and friends: signed-in users skip straight to the app. */
export function RedirectIfSignedIn({ children }: { children?: ReactNode }) {
  const { me, status, needsTwoFactorSetup } = useAuth()
  const [params] = useSearchParams()
  if (status === 'loading') return <PageSpinner />
  if (me) return <Navigate to={needsTwoFactorSetup ? '/setup-2fa' : safeNext(params.get('next'))} replace />
  return children ?? <Outlet />
}
