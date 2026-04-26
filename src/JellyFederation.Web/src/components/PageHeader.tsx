interface PageHeaderProps {
  eyebrow?: string
  title: string
  description?: React.ReactNode
  icon?: React.ReactNode
  action?: React.ReactNode
  meta?: React.ReactNode
  className?: string
}

export function PageHeader({ eyebrow, title, description, icon, action, meta, className = '' }: PageHeaderProps) {
  return (
    <div className={`mb-8 flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between ${className}`}>
      <div className="min-w-0">
        {eyebrow && (
          <p className="mb-2 text-xs font-semibold uppercase tracking-[0.18em] text-[var(--color-accent)]">
            {eyebrow}
          </p>
        )}
        <div className="flex min-w-0 items-center gap-3">
          {icon && (
            <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border border-[var(--color-accent)]/20 bg-[var(--color-accent-dim)] text-[var(--color-accent)]">
              {icon}
            </span>
          )}
          <div className="min-w-0">
            <h1 className="truncate text-3xl font-semibold tracking-tight text-[var(--color-heading)]">{title}</h1>
            {description && <p className="mt-1 max-w-2xl text-sm leading-6 text-[var(--color-text)]">{description}</p>}
          </div>
        </div>
        {meta && <div className="mt-3 text-sm text-[var(--color-text)]">{meta}</div>}
      </div>
      {action && <div className="shrink-0">{action}</div>}
    </div>
  )
}
