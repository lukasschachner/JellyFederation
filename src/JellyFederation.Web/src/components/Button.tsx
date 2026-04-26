import { forwardRef } from 'react'
import { Loader2 } from 'lucide-react'

interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'primary' | 'secondary' | 'danger' | 'ghost'
  size?: 'sm' | 'md'
  loading?: boolean
}

const variants = {
  primary: 'bg-[var(--color-accent)] text-white hover:opacity-90 active:brightness-90',
  secondary: 'bg-white/5 border border-[var(--color-border)] text-[var(--color-heading)] hover:bg-white/10 active:bg-white/15',
  danger: 'bg-red-950/60 border border-red-900/50 text-red-400 hover:bg-red-950 active:bg-red-900/70',
  ghost: 'text-[var(--color-text)] hover:bg-white/5 hover:text-[var(--color-heading)] active:bg-white/10',
}

const sizes = {
  sm: 'px-3 py-1.5 text-sm',
  md: 'px-4 py-2 text-sm',
}

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(function Button({
  variant = 'secondary',
  size = 'md',
  loading,
  disabled,
  children,
  className = '',
  ...props
}, ref) {
  return (
    <button
      {...props}
      ref={ref}
      disabled={disabled || loading}
      aria-busy={loading ? true : undefined}
      className={`inline-flex items-center gap-2 rounded-lg font-medium transition-all duration-150 ease-out cursor-pointer select-none disabled:opacity-50 disabled:cursor-not-allowed active:scale-[0.97] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--color-focus)] focus-visible:ring-offset-2 focus-visible:ring-offset-[var(--color-bg)] ${variants[variant]} ${sizes[size]} ${className}`}
    >
      {loading && <Loader2 size={14} className="animate-spin" />}
      {children}
    </button>
  )
})
