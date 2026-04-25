import { Search } from 'lucide-react'

interface MediaSearchBoxProps {
  value: string
  onChange: (value: string) => void
  label: string
  placeholder?: string
  className?: string
}

export function MediaSearchBox({
  value,
  onChange,
  label,
  placeholder = 'Search titles…',
  className = 'mb-6',
}: MediaSearchBoxProps) {
  return (
    <div className={`relative ${className}`}>
      <Search size={15} className="absolute left-3 top-1/2 -translate-y-1/2 text-[var(--color-text)]" />
      <input
        aria-label={label}
        value={value}
        onChange={e => onChange(e.target.value)}
        placeholder={placeholder}
        className="w-full bg-[var(--color-surface)] border border-[var(--color-border)] rounded-lg pl-9 pr-4 py-2.5 text-sm text-[var(--color-heading)] placeholder:text-[var(--color-text)] outline-none focus:ring-2 focus:ring-[var(--color-accent)]/40 transition"
      />
    </div>
  )
}
