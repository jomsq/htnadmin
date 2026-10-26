import { QueryClient } from '@tanstack/react-query'
import { isApiError } from './api'

export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        refetchOnWindowFocus: false,
        // Do not retry client errors (401/403/404/400); retry network/5xx once.
        retry: (count, error) => !(isApiError(error) && error.status < 500) && count < 1,
      },
      mutations: { retry: false },
    },
  })
}
