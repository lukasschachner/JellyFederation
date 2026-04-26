import { useQuery } from '@tanstack/react-query'
import { BookOpen, Download, Mail, Server, Wifi, WifiOff } from 'lucide-react'
import { fileRequestsApi, invitationsApi, libraryApi } from '../api/client'
import { Badge } from '../components/Badge'
import { Card } from '../components/Card'
import { CopyButton } from '../components/CopyButton'
import { PageHeader } from '../components/PageHeader'
import { StatCard } from '../components/StatCard'
import { useConfig } from '../hooks/useConfig'
import type { ConnectionState } from '../hooks/useSignalR'

interface DashboardProps {
  connectionState: ConnectionState
}

export function Dashboard({ connectionState }: DashboardProps) {
  const cfg = useConfig()
  const myServerId = cfg?.serverId?.toLowerCase() ?? ''
  const { data: libraryCounts } = useQuery({ queryKey: ['library-counts'], queryFn: () => libraryApi.browseCounts() })
  const { data: invitations } = useQuery({ queryKey: ['invitations'], queryFn: invitationsApi.list })
  const { data: requests } = useQuery({ queryKey: ['requests'], queryFn: fileRequestsApi.list })

  const pendingInvitations = invitations?.filter(i => i.status === 'Pending' && i.toServerId.toLowerCase() === myServerId) ?? []
  const activeRequests = requests?.filter(r => r.status === 'Pending' || r.status === 'HolePunching' || r.status === 'Transferring') ?? []
  const connected = connectionState === 'connected'

  return (
    <div className="max-w-5xl">
      <PageHeader
        eyebrow="Overview"
        title="Dashboard"
        description={cfg?.serverName ?? 'Federation server overview'}
        icon={<Server size={18} />}
      />

      <Card className="mb-6 overflow-hidden bg-gradient-to-br from-white/[0.04] to-transparent">
        <div className="flex flex-col gap-4 sm:flex-row sm:items-center">
          <div className={`flex h-12 w-12 items-center justify-center rounded-2xl border ${connected ? 'border-emerald-500/20 bg-emerald-500/15' : 'border-red-500/20 bg-red-500/15'}`}>
            {connected
              ? <Wifi size={20} className="text-emerald-400" />
              : <WifiOff size={20} className="text-red-400" />}
          </div>
          <div className="min-w-0 flex-1">
            <p className="text-sm font-medium text-[var(--color-heading)]">Federation Server</p>
            <p className="mt-0.5 truncate text-xs text-[var(--color-text)]">{cfg?.serverUrl}</p>
          </div>
          <Badge variant={connected ? 'success' : 'danger'}>{connectionState}</Badge>
        </div>
      </Card>

      <div className="mb-8 grid gap-4 sm:grid-cols-3">
        <StatCard label="Federated Items" value={libraryCounts?.['All'] ?? 0} icon={<BookOpen size={17} />} />
        <StatCard label="Pending Invitations" value={pendingInvitations.length} icon={<Mail size={17} />} tone="warning" />
        <StatCard label="Active Transfers" value={activeRequests.length} icon={<Download size={17} />} tone="success" />
      </div>

      <Card className="bg-gradient-to-b from-white/[0.03] to-transparent">
        <div className="flex items-start gap-3">
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border border-[var(--color-accent)]/20 bg-[var(--color-accent-dim)]">
            <Server size={17} className="text-[var(--color-accent)]" />
          </div>
          <div className="min-w-0 flex-1">
            <p className="text-sm font-medium text-[var(--color-heading)]">Your Server</p>
            <div className="mt-3 grid gap-2 text-xs">
              <div className="grid gap-2 sm:grid-cols-[80px_minmax(0,1fr)_auto] sm:items-center">
                <span className="text-[var(--color-text)]">Server ID</span>
                <code className="min-w-0 truncate rounded-lg bg-white/5 px-2 py-1 font-mono text-[var(--color-heading)]">{cfg?.serverId}</code>
                <CopyButton value={cfg?.serverId} label="Copy ID" successMessage="Server ID copied" />
              </div>
              <div className="grid gap-2 sm:grid-cols-[80px_minmax(0,1fr)_auto] sm:items-center">
                <span className="text-[var(--color-text)]">API Key</span>
                <code className="min-w-0 truncate rounded-lg bg-white/5 px-2 py-1 font-mono tracking-[0.18em] text-[var(--color-heading)]">
                  {cfg?.apiKey ? '•'.repeat(20) : 'Stored in secure browser session'}
                </code>
                <CopyButton value={cfg?.apiKey} label="Copy key" successMessage="API key copied" />
              </div>
            </div>
            <p className="mt-3 text-xs leading-5 text-[var(--color-text)]">
              Share your Server ID with others so they can send you invitations. Keep your API key private.
            </p>
          </div>
        </div>
      </Card>
    </div>
  )
}
