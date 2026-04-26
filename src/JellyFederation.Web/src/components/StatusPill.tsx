import { AlertCircle, CheckCircle2, Clock, Loader2, Radio, X } from 'lucide-react'
import type { FileRequestStatus } from '../api/types'

export type GenericStatus = FileRequestStatus | 'Accepted' | 'Declined' | 'Revoked'

interface StatusPillProps {
  status: GenericStatus
  className?: string
}

const styles: Record<GenericStatus, string> = {
  Pending: 'bg-[var(--color-warning-bg)] text-[var(--color-warning)] border-yellow-500/20',
  HolePunching: 'bg-[var(--color-accent-dim)] text-[var(--color-accent)] border-[var(--color-accent)]/20',
  Transferring: 'bg-[var(--color-accent-dim)] text-[var(--color-accent)] border-[var(--color-accent)]/20',
  Completed: 'bg-[var(--color-success-bg)] text-[var(--color-success)] border-emerald-500/20',
  Failed: 'bg-[var(--color-danger-bg)] text-[var(--color-danger)] border-red-500/20',
  Cancelled: 'bg-white/5 text-[var(--color-text)] border-white/10',
  Accepted: 'bg-[var(--color-success-bg)] text-[var(--color-success)] border-emerald-500/20',
  Declined: 'bg-[var(--color-danger-bg)] text-[var(--color-danger)] border-red-500/20',
  Revoked: 'bg-white/5 text-[var(--color-text)] border-white/10',
}

const icons: Record<GenericStatus, React.ReactNode> = {
  Pending: <Clock size={12} />,
  HolePunching: <Radio size={12} className="animate-pulse" />,
  Transferring: <Loader2 size={12} className="animate-spin" />,
  Completed: <CheckCircle2 size={12} />,
  Failed: <AlertCircle size={12} />,
  Cancelled: <X size={12} />,
  Accepted: <CheckCircle2 size={12} />,
  Declined: <X size={12} />,
  Revoked: <X size={12} />,
}

export function StatusPill({ status, className = '' }: StatusPillProps) {
  return (
    <span className={`inline-flex items-center gap-1.5 rounded-full border px-2.5 py-1 text-xs font-medium ${styles[status]} ${className}`}>
      {icons[status]}
      {status}
    </span>
  )
}
