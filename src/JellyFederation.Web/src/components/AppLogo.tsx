interface AppLogoProps {
  size?: 'sm' | 'md' | 'lg'
  className?: string
}

const sizes = {
  sm: 'h-8 w-8 rounded-lg',
  md: 'h-10 w-10 rounded-xl',
  lg: 'h-12 w-12 rounded-2xl',
}

export function AppLogo({ size = 'md', className = '' }: AppLogoProps) {
  return (
    <img
      src="/icon.png"
      alt="JellyFederation logo"
      className={`${sizes[size]} object-cover shadow-sm shadow-black/30 ring-1 ring-white/10 ${className}`}
      draggable={false}
    />
  )
}
