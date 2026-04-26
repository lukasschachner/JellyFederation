import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { AlertCircle, CheckCircle2, Info, X } from 'lucide-react'
import {
  ToastContext,
  type Toast,
  type ToastContextValue,
  type ToastInput,
  type ToastVariant,
} from './ToastContext'

const DEFAULT_DURATION = 4000
const MAX_TOASTS = 4

const variantConfig: Record<ToastVariant, {
  icon: React.ReactNode
  ring: string
  iconWrap: string
  bar: string
}> = {
  success: {
    icon: <CheckCircle2 size={16} className="text-emerald-400" />,
    ring: 'border-emerald-500/30',
    iconWrap: 'bg-emerald-500/15',
    bar: 'bg-emerald-400',
  },
  error: {
    icon: <AlertCircle size={16} className="text-red-400" />,
    ring: 'border-red-500/30',
    iconWrap: 'bg-red-500/15',
    bar: 'bg-red-400',
  },
  info: {
    icon: <Info size={16} className="text-[var(--color-accent)]" />,
    ring: 'border-[var(--color-accent)]/30',
    iconWrap: 'bg-[var(--color-accent-dim)]',
    bar: 'bg-[var(--color-accent)]',
  },
}

let toastSeq = 0

export function ToastProvider({ children }: { children: React.ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([])
  const timers = useRef<Map<string, number>>(new Map())

  const clearTimer = useCallback((id: string) => {
    const handle = timers.current.get(id)
    if (handle !== undefined) {
      window.clearTimeout(handle)
      timers.current.delete(id)
    }
  }, [])

  const dismiss = useCallback((id: string) => {
    setToasts(prev => prev.filter(t => t.id !== id))
    clearTimer(id)
  }, [clearTimer])

  const scheduleDismiss = useCallback((toast: Toast) => {
    if (toast.durationMs <= 0) return
    clearTimer(toast.id)
    const handle = window.setTimeout(() => dismiss(toast.id), toast.durationMs)
    timers.current.set(toast.id, handle)
  }, [clearTimer, dismiss])

  const show = useCallback((input: ToastInput): string => {
    const id = `t-${++toastSeq}-${Date.now()}`
    const toast: Toast = {
      id,
      variant: input.variant ?? 'info',
      title: input.title,
      description: input.description,
      durationMs: input.durationMs ?? DEFAULT_DURATION,
    }
    setToasts(prev => [...prev, toast].slice(-MAX_TOASTS))
    scheduleDismiss(toast)
    return id
  }, [scheduleDismiss])

  useEffect(() => {
    const map = timers.current
    return () => {
      map.forEach(handle => window.clearTimeout(handle))
      map.clear()
    }
  }, [])

  const value = useMemo<ToastContextValue>(() => ({
    show,
    dismiss,
    success: (title, description) => show({ variant: 'success', title, description }),
    error: (title, description) => show({ variant: 'error', title, description, durationMs: 8000 }),
    info: (title, description) => show({ variant: 'info', title, description }),
  }), [show, dismiss])

  return (
    <ToastContext.Provider value={value}>
      {children}
      <ToastViewport toasts={toasts} onDismiss={dismiss} onPause={clearTimer} onResume={(id) => {
        const toast = toasts.find(t => t.id === id)
        if (toast) scheduleDismiss(toast)
      }} />
    </ToastContext.Provider>
  )
}

function ToastViewport({
  toasts,
  onDismiss,
  onPause,
  onResume,
}: {
  toasts: Toast[]
  onDismiss: (id: string) => void
  onPause: (id: string) => void
  onResume: (id: string) => void
}) {
  return (
    <div
      aria-live="polite"
      aria-atomic="false"
      className="pointer-events-none fixed bottom-4 right-4 z-[100] flex w-[min(92vw,360px)] flex-col gap-2"
    >
      {toasts.map(toast => (
        <ToastItem key={toast.id} toast={toast} onDismiss={onDismiss} onPause={onPause} onResume={onResume} />
      ))}
    </div>
  )
}

function ToastItem({
  toast,
  onDismiss,
  onPause,
  onResume,
}: {
  toast: Toast
  onDismiss: (id: string) => void
  onPause: (id: string) => void
  onResume: (id: string) => void
}) {
  const cfg = variantConfig[toast.variant]
  return (
    <div
      role={toast.variant === 'error' ? 'alert' : 'status'}
      onMouseEnter={() => onPause(toast.id)}
      onMouseLeave={() => onResume(toast.id)}
      className={`pointer-events-auto relative overflow-hidden rounded-xl border ${cfg.ring} bg-[var(--color-surface)] shadow-lg shadow-black/40 jf-toast-enter`}
    >
      <div className="flex items-start gap-3 p-3 pr-9">
        <div className={`mt-0.5 flex h-7 w-7 shrink-0 items-center justify-center rounded-lg ${cfg.iconWrap}`}>
          {cfg.icon}
        </div>
        <div className="min-w-0 flex-1">
          <p className="text-sm font-medium text-[var(--color-heading)] leading-snug">{toast.title}</p>
          {toast.description && (
            <p className="mt-0.5 whitespace-pre-line break-words text-xs leading-snug text-[var(--color-text)]">{toast.description}</p>
          )}
        </div>
        <button
          type="button"
          aria-label="Dismiss notification"
          onClick={() => onDismiss(toast.id)}
          className="absolute right-2 top-2 inline-flex h-6 w-6 items-center justify-center rounded-md text-[var(--color-text)] hover:bg-white/5 hover:text-[var(--color-heading)] cursor-pointer"
        >
          <X size={13} />
        </button>
      </div>
      {toast.durationMs > 0 && (
        <span
          className={`absolute bottom-0 left-0 h-0.5 ${cfg.bar} jf-toast-bar`}
          style={{ animationDuration: `${toast.durationMs}ms` }}
        />
      )}
    </div>
  )
}
