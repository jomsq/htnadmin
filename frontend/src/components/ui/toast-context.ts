import { createContext, useContext } from 'react'

export type ToastTone = 'success' | 'error' | 'info'
export type ToastInput = { title: string; description?: string; tone?: ToastTone }
export type ToastApi = { toast: (t: ToastInput) => void }

export const ToastContext = createContext<ToastApi | null>(null)

export function useToast(): ToastApi {
  const ctx = useContext(ToastContext)
  if (!ctx) throw new Error('useToast must be used inside <ToastProvider>')
  return ctx
}
