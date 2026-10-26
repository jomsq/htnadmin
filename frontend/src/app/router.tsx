import { createBrowserRouter, type RouteObject } from 'react-router'
import { RedirectIfSignedIn, RequireAuth } from './auth/RequireAuth'
import { RequirePolicy } from './auth/RequirePolicy'
import { Policies } from './auth/policies'
import { AppLayout } from './layouts/AppLayout'
import { ComingSoonPage } from './pages/ComingSoonPage'
import { NotFoundPage } from './pages/NotFoundPage'
import { RouteErrorPage } from './pages/RouteErrorPage'

const comingSoon = (path: string, title: string, description: string): RouteObject => ({
  path,
  element: <ComingSoonPage title={title} description={description} />,
})

export const routes: RouteObject[] = [
  {
    errorElement: <RouteErrorPage />,
    children: [
      {
        element: <RedirectIfSignedIn />,
        children: [
          { path: '/login', lazy: async () => ({ Component: (await import('@/features/auth/LoginPage')).LoginPage }) },
          { path: '/forgot', lazy: async () => ({ Component: (await import('@/features/auth/ForgotPasswordPage')).ForgotPasswordPage }) },
          { path: '/reset/:token', lazy: async () => ({ Component: (await import('@/features/auth/ResetPasswordPage')).ResetPasswordPage }) },
          { path: '/invite/:token', lazy: async () => ({ Component: (await import('@/features/auth/InvitePage')).InvitePage }) },
        ],
      },
      {
        element: <RequireAuth allowTwoFactorSetup />,
        children: [
          {
            path: '/setup-2fa',
            lazy: async () => ({ Component: (await import('@/features/auth/SetupTwoFactorPage')).SetupTwoFactorPage }),
          },
        ],
      },
      {
        element: <RequireAuth />,
        children: [
          {
            element: <AppLayout />,
            children: [
              { index: true, lazy: async () => ({ Component: (await import('@/features/dashboard/DashboardPage')).DashboardPage }) },
              {
                path: '/people',
                element: <RequirePolicy policy={Policies.PeopleView} />,
                children: [{ index: true, lazy: async () => ({ Component: (await import('@/features/organization/PeoplePage')).PeoplePage }) }],
              },
              { path: '/settings', lazy: async () => ({ Component: (await import('@/features/settings/SettingsPage')).SettingsPage }) },
              comingSoon('/events', 'Events', 'Events, tickets, committees, registrations and check-in.'),
              comingSoon('/directory', 'Directory', 'Agencies, companies, universities, non-profits and suppliers.'),
              comingSoon('/store', 'Merch store', 'Leftover stock and pop-up stores.'),
              {
                path: '/finance',
                element: <RequirePolicy policy={Policies.FinanceView} />,
                children: [{ index: true, element: <ComingSoonPage title="Finance" description="Cashflow, ledger, balancing and audit. Board only." /> }],
              },
              comingSoon('/tasks', 'My tasks', 'Tasks assigned to you across committees.'),
              comingSoon('/leaderboard', 'Leaderboard', 'Points for goons and apprentices by event, quarter or all time.'),
              { path: '*', element: <NotFoundPage /> },
            ],
          },
        ],
      },
    ],
  },
]

export function createRouter() {
  return createBrowserRouter(routes)
}
