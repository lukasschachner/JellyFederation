import { Link, useLocation } from 'react-router-dom'
import {
  BookOpen,
  HardDrive,
  LayoutDashboard,
  Mail,
  Settings,
  Share2,
  Download,
} from 'lucide-react'
import type { ConnectionState } from '../hooks/useSignalR'

interface LayoutProps {
  children: React.ReactNode
  connectionState: ConnectionState
}

const nav = [
  { to: '/', label: 'Dashboard', icon: LayoutDashboard },
  { to: '/my-media', label: 'My Media', icon: HardDrive },
  { to: '/library', label: 'Library', icon: BookOpen },
  { to: '/invitations', label: 'Invitations', icon: Mail },
  { to: '/requests', label: 'Requests', icon: Download },
  { to: '/settings', label: 'Settings', icon: Settings },
]

const stateColor: Record<ConnectionState, string> = {
  connected: 'bg-emerald-400',
  connecting: 'bg-yellow-400 animate-pulse',
  reconnecting: 'bg-yellow-400 animate-pulse',
  disconnected: 'bg-red-400',
}

export function Layout({ children, connectionState }: LayoutProps) {
  const { pathname } = useLocation()

  return (
    <div className="flex min-h-screen">
      {/* Sidebar */}
      <aside className="w-56 shrink-0 flex flex-col border-r border-[var(--color-border)] bg-[var(--color-surface)]">
        <div className="p-5 border-b border-[var(--color-border)]">
          <div className="flex items-center gap-2.5">
            <Share2 size={20} className="text-[var(--color-accent)]" />
            <span className="font-semibold text-[var(--color-heading)] text-sm">JellyFederation</span>
          </div>
        </div>

        <nav className="flex-1 p-3 flex flex-col gap-0.5">
          {nav.map(({ to, label, icon: Icon }) => {
            const active = pathname === to
            return (
              <Link
                key={to}
                to={to}
                className={`flex items-center gap-2.5 px-3 py-2 rounded-lg text-sm transition-colors ${
                  active
                    ? 'bg-[var(--color-accent-dim)] text-[var(--color-accent)] font-medium'
                    : 'text-[var(--color-text)] hover:bg-white/5 hover:text-[var(--color-heading)]'
                }`}
              >
                <Icon size={15} />
                {label}
              </Link>
            )
          })}
        </nav>

        <div className="p-4 border-t border-[var(--color-border)]">
          <div className="flex items-center gap-2 text-xs text-[var(--color-text)]">
            <span className={`w-2 h-2 rounded-full shrink-0 ${stateColor[connectionState]}`} />
            <span className="capitalize">{connectionState}</span>
          </div>
        </div>
      </aside>

      {/* Main */}
      <main className="flex-1 p-8 overflow-auto">
        {children}
      </main>
    </div>
  )
}
