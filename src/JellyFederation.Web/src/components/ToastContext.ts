import { createContext } from 'react'

export type ToastVariant = 'success' | 'error' | 'info'

export interface Toast {
  id: string
  variant: ToastVariant
  title: string
  description?: string
  durationMs: number
}

export interface ToastInput {
  variant?: ToastVariant
  title: string
  description?: string
  durationMs?: number
}

export interface ToastContextValue {
  show: (input: ToastInput) => string
  success: (title: string, description?: string) => string
  error: (title: string, description?: string) => string
  info: (title: string, description?: string) => string
  dismiss: (id: string) => void
}

export const ToastContext = createContext<ToastContextValue | null>(null)
