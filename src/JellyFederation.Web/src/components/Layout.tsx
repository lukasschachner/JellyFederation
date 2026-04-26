import { Link, useLocation } from 'react-router-dom'
import {
  BookOpen,
  HardDrive,
  LayoutDashboard,
  Mail,
  Settings,
  Download,
} from 'lucide-react'
import { AppLogo } from './AppLogo'
import type { ConnectionState } from '../hooks/useSignalR'

interface LayoutProps {
  children: React.ReactNode
  connectionState: ConnectionState
}

const navSections = [
  {
    label: 'Overview',
    items: [{ to: '/', label: 'Dashboard', icon: LayoutDashboard }],
  },
  {
    label: 'Media',
    items: [
      { to: '/my-media', label: 'My Media', icon: HardDrive },
      { to: '/library', label: 'Library', icon: BookOpen },
      { to: '/requests', label: 'Requests', icon: Download },
    ],
  },
  {
    label: 'Federation',
    items: [{ to: '/invitations', label: 'Invitations', icon: Mail }],
  },
  {
    label: 'System',
    items: [{ to: '/settings', label: 'Settings', icon: Settings }],
  },
]

const mobileNav = navSections.flatMap(section => section.items)

const stateColor: Record<ConnectionState, string> = {
  connected: 'bg-emerald-400',
  connecting: 'bg-yellow-400 animate-pulse',
  reconnecting: 'bg-yellow-400 animate-pulse',
  disconnected: 'bg-red-400',
}

function NavLink({ to, label, icon: Icon, compact = false }: { to: string; label: string; icon: React.ComponentType<{ size?: number }>; compact?: boolean }) {
  const { pathname } = useLocation()
  const active = pathname === to

  return (
    <Link
      to={to}
      aria-current={active ? 'page' : undefined}
      className={`group relative flex items-center gap-2.5 rounded-lg text-sm transition-all duration-150 active:scale-[0.99] ${compact ? 'px-2.5 py-2' : 'px-3 py-2'} ${
        active
          ? 'bg-[var(--color-accent-dim)] text-[var(--color-accent)] font-medium shadow-sm shadow-[var(--color-accent)]/10'
          : 'text-[var(--color-text)] hover:bg-white/5 hover:text-[var(--color-heading)]'
      }`}
    >
      {active && !compact && <span className="absolute left-0 top-2 bottom-2 w-0.5 rounded-full bg-[var(--color-accent)]" />}
      <span className={`flex h-6 w-6 items-center justify-center rounded-md transition-colors ${active ? 'bg-[var(--color-accent)]/15' : 'bg-white/[0.03] group-hover:bg-white/[0.06]'}`}>
        <Icon size={15} />
      </span>
      <span className={compact ? 'hidden min-[430px]:inline' : ''}>{label}</span>
    </Link>
  )
}

export function Layout({ children, connectionState }: LayoutProps) {
  return (
    <div className="flex min-h-screen bg-[radial-gradient(circle_at_top_left,rgba(129,140,248,.08),transparent_34rem),var(--color-bg)]">
      <aside className="hidden w-56 shrink-0 flex-col border-r border-[var(--color-border)] bg-[var(--color-surface)]/95 backdrop-blur md:flex">
        <div className="p-5 border-b border-[var(--color-border)]">
          <div className="flex items-center gap-2.5">
            <AppLogo size="sm" />
            <span className="font-semibold text-[var(--color-heading)] text-sm">JellyFederation</span>
          </div>
        </div>

        <nav className="flex-1 p-3 flex flex-col gap-5">
          {navSections.map(section => (
            <div key={section.label}>
              <p className="mb-2 px-3 text-[10px] font-semibold uppercase tracking-[0.18em] text-[var(--color-text)]/70">
                {section.label}
              </p>
              <div className="flex flex-col gap-1">
                {section.items.map(item => <NavLink key={item.to} {...item} />)}
              </div>
            </div>
          ))}
        </nav>

        <div className="p-4 border-t border-[var(--color-border)]">
          <div className="flex items-center justify-between rounded-xl border border-[var(--color-border)] bg-black/15 px-3 py-2 text-xs text-[var(--color-text)]">
            <span>SignalR</span>
            <span className="flex items-center gap-2 capitalize">
              <span className={`w-2 h-2 rounded-full shrink-0 ${stateColor[connectionState]}`} />
              {connectionState}
            </span>
          </div>
        </div>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="sticky top-0 z-40 border-b border-[var(--color-border)] bg-[var(--color-surface)]/95 px-3 py-2 backdrop-blur md:hidden">
          <div className="mb-2 flex items-center justify-between px-1">
            <div className="flex items-center gap-2 text-sm font-semibold text-[var(--color-heading)]">
              <AppLogo size="sm" className="h-7 w-7" />
              JellyFederation
            </div>
            <span className="flex items-center gap-2 rounded-full border border-[var(--color-border)] bg-black/15 px-2 py-1 text-xs capitalize text-[var(--color-text)]">
              <span className={`h-2 w-2 rounded-full ${stateColor[connectionState]}`} />
              {connectionState}
            </span>
          </div>
          <nav className="flex gap-1 overflow-x-auto pb-1">
            {mobileNav.map(item => <NavLink key={item.to} {...item} compact />)}
          </nav>
        </header>

        <main className="flex-1 overflow-auto p-4 sm:p-6 lg:p-8">
          <div className="animate-[jf-page-in_180ms_ease-out]">
            {children}
          </div>
        </main>
      </div>
    </div>
  )
}
