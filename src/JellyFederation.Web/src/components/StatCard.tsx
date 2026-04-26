import { Card } from './Card'

interface StatCardProps {
  label: string
  value: React.ReactNode
  icon: React.ReactNode
  tone?: 'accent' | 'success' | 'warning' | 'danger'
}

const tones = {
  accent: 'bg-[var(--color-accent-dim)] text-[var(--color-accent)] border-[var(--color-accent)]/20',
  success: 'bg-emerald-500/15 text-emerald-400 border-emerald-500/20',
  warning: 'bg-yellow-500/15 text-yellow-400 border-yellow-500/20',
  danger: 'bg-red-500/15 text-red-400 border-red-500/20',
}

export function StatCard({ label, value, icon, tone = 'accent' }: StatCardProps) {
  return (
    <Card className="bg-gradient-to-b from-white/[0.03] to-transparent">
      <div className={`mb-3 flex h-10 w-10 items-center justify-center rounded-xl border ${tones[tone]}`}>
        {icon}
      </div>
      <p className="text-2xl font-semibold tracking-tight text-[var(--color-heading)]">{value}</p>
      <p className="mt-0.5 text-xs text-[var(--color-text)]">{label}</p>
    </Card>
  )
}
