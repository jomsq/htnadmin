import type { ReactElement } from 'react'
import { render } from '@testing-library/react'
import { createMemoryRouter, RouterProvider, type RouteObject } from 'react-router'
import { QueryClient } from '@tanstack/react-query'
import { Providers } from '@/app/Providers'

export function createTestQueryClient() {
  return new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity }, mutations: { retry: false } } })
}

/** Render routes inside the real providers with a memory router. */
export function renderRoutes(routes: RouteObject[], initialPath = '/') {
  const client = createTestQueryClient()
  const router = createMemoryRouter(routes, { initialEntries: [initialPath] })
  const utils = render(
    <Providers client={client}>
      <RouterProvider router={router} />
    </Providers>,
  )
  return { ...utils, router, client }
}

/** Render a single element at "/" inside the real providers. */
export function renderWithProviders(ui: ReactElement, initialPath = '/') {
  return renderRoutes([{ path: '*', element: ui }], initialPath)
}
