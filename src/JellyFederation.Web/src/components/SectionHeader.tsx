interface SectionHeaderProps {
  title: string
  icon?: React.ReactNode
  count?: number
  description?: React.ReactNode
  action?: React.ReactNode
  className?: string
}

export function SectionHeader({ title, icon, count, description, action, className = '' }: SectionHeaderProps) {
  return (
    <div className={`mb-3 flex items-start justify-between gap-3 ${className}`}>
      <div className="min-w-0">
        <h2 className="flex items-center gap-2 text-sm font-semibold text-[var(--color-heading)]">
          {icon && <span className="text-[var(--color-accent)]">{icon}</span>}
          {title}
          {count !== undefined && (
            <span className="rounded-full bg-white/5 px-2 py-0.5 text-xs font-normal text-[var(--color-text)]">
              {count}
            </span>
          )}
        </h2>
        {description && <p className="mt-1 text-xs leading-5 text-[var(--color-text)]">{description}</p>}
      </div>
      {action && <div className="shrink-0">{action}</div>}
    </div>
  )
}
