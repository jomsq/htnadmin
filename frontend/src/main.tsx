import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { RouterProvider } from 'react-router/dom'
import './index.css'
import { Providers } from './app/Providers'
import { createQueryClient } from './app/queryClient'
import { createRouter } from './app/router'

const queryClient = createQueryClient()
const router = createRouter()

const root = document.getElementById('root')
if (!root) throw new Error('Missing #root element')

createRoot(root).render(
  <StrictMode>
    <Providers client={queryClient}>
      <RouterProvider router={router} />
    </Providers>
  </StrictMode>,
)
