interface BadgeProps {
  variant?: 'default' | 'success' | 'warning' | 'danger' | 'neutral'
  children: React.ReactNode
}

const styles: Record<NonNullable<BadgeProps['variant']>, string> = {
  default: 'bg-[var(--color-accent-dim)] text-[var(--color-accent)]',
  success: 'bg-emerald-950/50 text-emerald-400',
  warning: 'bg-yellow-950/50 text-yellow-400',
  danger: 'bg-red-950/50 text-red-400',
  neutral: 'bg-white/5 text-[var(--color-text)]',
}

export function Badge({ variant = 'default', children }: BadgeProps) {
  return (
    <span className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium ${styles[variant]}`}>
      {children}
    </span>
  )
}
