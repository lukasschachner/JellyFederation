interface InputProps extends React.InputHTMLAttributes<HTMLInputElement> {
  label?: string
  hint?: string
  error?: string
}

export function Input({ label, hint, error, className = '', ...props }: InputProps) {
  return (
    <div className="flex flex-col gap-1.5">
      {label && (
        <label className="text-sm font-medium text-[var(--color-heading)]">
          {label}
        </label>
      )}
      <input
        {...props}
        className={`bg-[var(--color-surface)] border rounded-lg px-3 py-2 text-sm text-[var(--color-heading)] placeholder:text-[var(--color-text)] outline-none focus:ring-2 focus:ring-[var(--color-accent)]/40 transition ${error ? 'border-red-500' : 'border-[var(--color-border)]'} ${className}`}
      />
      {hint && !error && <p className="text-xs text-[var(--color-text)]">{hint}</p>}
      {error && <p className="text-xs text-red-400">{error}</p>}
    </div>
  )
}
