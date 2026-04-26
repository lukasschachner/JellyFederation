import { useEffect, useRef } from 'react'
import { AlertTriangle, X } from 'lucide-react'
import { Button } from './Button'
import { IconButton } from './IconButton'

interface ConfirmDialogProps {
  open: boolean
  title: string
  description: React.ReactNode
  confirmLabel?: string
  cancelLabel?: string
  variant?: 'danger' | 'primary'
  onConfirm: () => void | Promise<void>
  onCancel: () => void
}

export function ConfirmDialog({
  open,
  title,
  description,
  confirmLabel = 'Confirm',
  cancelLabel = 'Cancel',
  variant = 'primary',
  onConfirm,
  onCancel,
}: ConfirmDialogProps) {
  const cancelRef = useRef<HTMLButtonElement | null>(null)
  const previousFocusRef = useRef<HTMLElement | null>(null)

  useEffect(() => {
    if (!open) return
    previousFocusRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null
    const previousOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    window.setTimeout(() => cancelRef.current?.focus(), 0)

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        event.preventDefault()
        onCancel()
      }
      if (event.key !== 'Tab') return
      const focusables = Array.from(document.querySelectorAll<HTMLElement>('[data-confirm-dialog] button:not(:disabled)'))
      if (focusables.length === 0) return
      const first = focusables[0]
      const last = focusables[focusables.length - 1]
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault()
        last.focus()
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault()
        first.focus()
      }
    }

    document.addEventListener('keydown', handleKeyDown)
    return () => {
      document.removeEventListener('keydown', handleKeyDown)
      document.body.style.overflow = previousOverflow
      previousFocusRef.current?.focus()
    }
  }, [open, onCancel])

  if (!open) return null

  return (
    <div className="fixed inset-0 z-[90] flex items-center justify-center bg-black/60 p-4 backdrop-blur-sm" role="presentation" onMouseDown={onCancel}>
      <div
        data-confirm-dialog
        role="dialog"
        aria-modal="true"
        aria-labelledby="confirm-dialog-title"
        className="w-full max-w-md rounded-2xl border border-[var(--color-border)] bg-[var(--color-surface)] p-5 shadow-2xl shadow-black/50 jf-toast-enter"
        onMouseDown={event => event.stopPropagation()}
      >
        <div className="mb-4 flex items-start gap-3">
          <div className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-xl ${variant === 'danger' ? 'bg-red-500/15 text-red-400' : 'bg-[var(--color-accent-dim)] text-[var(--color-accent)]'}`}>
            <AlertTriangle size={18} />
          </div>
          <div className="min-w-0 flex-1">
            <h2 id="confirm-dialog-title" className="text-base font-semibold text-[var(--color-heading)]">{title}</h2>
            <div className="mt-1 text-sm leading-6 text-[var(--color-text)]">{description}</div>
          </div>
          <IconButton label="Close dialog" icon={<X size={14} />} onClick={onCancel} />
        </div>
        <div className="flex justify-end gap-2">
          <Button ref={cancelRef} variant="secondary" onClick={onCancel}>{cancelLabel}</Button>
          <Button variant={variant === 'danger' ? 'danger' : 'primary'} onClick={onConfirm}>{confirmLabel}</Button>
        </div>
      </div>
    </div>
  )
}
