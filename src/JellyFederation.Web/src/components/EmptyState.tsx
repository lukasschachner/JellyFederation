import { Card } from './Card'

interface EmptyStateProps {
  icon?: React.ReactNode
  title: string
  description?: React.ReactNode
  action?: React.ReactNode
  className?: string
}

export function EmptyState({ icon, title, description, action, className = '' }: EmptyStateProps) {
  return (
    <Card className={`text-center py-14 ${className}`}>
      {icon && (
        <div className="mx-auto mb-4 flex h-12 w-12 items-center justify-center rounded-2xl border border-[var(--color-accent)]/20 bg-[var(--color-accent-dim)] text-[var(--color-accent)]">
          {icon}
        </div>
      )}
      <p className="font-medium text-[var(--color-heading)]">{title}</p>
      {description && <p className="mx-auto mt-1 max-w-md text-sm leading-6 text-[var(--color-text)]">{description}</p>}
      {action && <div className="mt-5 flex justify-center">{action}</div>}
    </Card>
  )
}
