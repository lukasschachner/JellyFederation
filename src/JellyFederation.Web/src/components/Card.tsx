interface CardProps {
  children: React.ReactNode
  className?: string
  variant?: 'default' | 'interactive' | 'muted' | 'danger'
}

const variants: Record<NonNullable<CardProps['variant']>, string> = {
  default: 'bg-[var(--color-surface)]',
  interactive: 'bg-[var(--color-surface)] hover:border-[var(--color-accent)]/45 hover:bg-[var(--color-surface-raised)] hover:-translate-y-0.5 cursor-default',
  muted: 'bg-[var(--color-surface-muted)]',
  danger: 'border-red-900/40 bg-[var(--color-danger-bg)]',
}

export function Card({ children, className = '', variant = 'default' }: CardProps) {
  return (
    <div className={`rounded-xl border border-[var(--color-border)] p-5 shadow-sm shadow-black/20 transition-all duration-150 ${variants[variant]} ${className}`}>
      {children}
    </div>
  )
}

export function CardHeader({ children, className = '' }: { children: React.ReactNode; className?: string }) {
  return <div className={`mb-4 ${className}`}>{children}</div>
}

export function CardTitle({ children, className = '' }: { children: React.ReactNode; className?: string }) {
  return <h2 className={`text-sm font-semibold text-[var(--color-heading)] ${className}`}>{children}</h2>
}

export function CardDescription({ children, className = '' }: { children: React.ReactNode; className?: string }) {
  return <p className={`mt-1 text-xs leading-5 text-[var(--color-text)] ${className}`}>{children}</p>
}

export function CardFooter({ children, className = '' }: { children: React.ReactNode; className?: string }) {
  return <div className={`mt-4 border-t border-[var(--color-border)] pt-4 ${className}`}>{children}</div>
}
