interface IconButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  label: string
  icon: React.ReactNode
  variant?: 'ghost' | 'danger'
}

const variants = {
  ghost: 'text-[var(--color-text)] hover:bg-white/5 hover:text-[var(--color-heading)]',
  danger: 'text-zinc-400 hover:bg-red-400/10 hover:text-red-400',
}

export function IconButton({ label, icon, variant = 'ghost', className = '', ...props }: IconButtonProps) {
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      className={`inline-flex h-8 w-8 items-center justify-center rounded-lg transition-all duration-150 active:scale-95 disabled:cursor-not-allowed disabled:opacity-40 ${variants[variant]} ${className}`}
      {...props}
    >
      {icon}
    </button>
  )
}
