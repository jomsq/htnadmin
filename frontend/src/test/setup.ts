import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterAll, afterEach, beforeAll } from 'vitest'
import { resetCsrfToken } from '@/app/api'
import { server } from './server'

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))
afterEach(() => {
  cleanup()
  server.resetHandlers()
  resetCsrfToken()
  window.localStorage.clear()
  const root = document.documentElement
  delete root.dataset.theme
  delete root.dataset.mode
  delete root.dataset.textSize
  delete root.dataset.density
})
afterAll(() => server.close())
