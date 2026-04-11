import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { BookOpen, Check, Copy, Download, Mail, Server, Wifi, WifiOff } from 'lucide-react'
import { fileRequestsApi } from '../api/client'
import { invitationsApi } from '../api/client'
import { libraryApi } from '../api/client'
import { loadConfig } from '../lib/config'
import { Card } from '../components/Card'
import { Badge } from '../components/Badge'
import type { ConnectionState } from '../hooks/useSignalR'

interface DashboardProps {
  connectionState: ConnectionState
}

function CopyButton({ value }: { value: string }) {
  const [copied, setCopied] = useState(false)

  function handleCopy() {
    navigator.clipboard.writeText(value)
    setCopied(true)
    setTimeout(() => setCopied(false), 2000)
  }

  return (
    <button
      onClick={handleCopy}
      title="Copy API key"
      className="p-1 rounded hover:bg-white/10 transition-colors text-[var(--color-text)] hover:text-[var(--color-heading)] cursor-pointer"
    >
      {copied ? <Check size={12} className="text-emerald-400" /> : <Copy size={12} />}
    </button>
  )
}

export function Dashboard({ connectionState }: DashboardProps) {
  const cfg = loadConfig()
  const { data: library } = useQuery({ queryKey: ['library'], queryFn: () => libraryApi.browse() })
  const { data: invitations } = useQuery({ queryKey: ['invitations'], queryFn: invitationsApi.list })
  const { data: requests } = useQuery({ queryKey: ['requests'], queryFn: fileRequestsApi.list })

  const pendingInvitations = invitations?.filter(i => i.status === 'Pending' && i.toServerId === cfg?.serverId) ?? []
  const activeRequests = requests?.filter(r => r.status === 'Pending' || r.status === 'HolePunching' || r.status === 'Transferring') ?? []

  const stats = [
    {
      label: 'Federated Items',
      value: library?.length ?? 0,
      icon: BookOpen,
      accent: 'text-[var(--color-accent)]',
      bg: 'bg-[var(--color-accent-dim)]',
    },
    {
      label: 'Pending Invitations',
      value: pendingInvitations.length,
      icon: Mail,
      accent: 'text-yellow-400',
      bg: 'bg-yellow-950/40',
    },
    {
      label: 'Active Transfers',
      value: activeRequests.length,
      icon: Download,
      accent: 'text-emerald-400',
      bg: 'bg-emerald-950/40',
    },
  ]

  return (
    <div className="max-w-4xl">
      <div className="mb-8">
        <h1 className="text-2xl font-semibold text-[var(--color-heading)]">Dashboard</h1>
        <p className="text-sm text-[var(--color-text)] mt-1">{cfg?.serverName}</p>
      </div>

      {/* Connection status */}
      <Card className="mb-6 flex items-center gap-4">
        <div className={`w-10 h-10 rounded-xl flex items-center justify-center ${
          connectionState === 'connected' ? 'bg-emerald-950/50' : 'bg-red-950/50'
        }`}>
          {connectionState === 'connected'
            ? <Wifi size={18} className="text-emerald-400" />
            : <WifiOff size={18} className="text-red-400" />}
        </div>
        <div>
          <p className="text-sm font-medium text-[var(--color-heading)]">Federation Server</p>
          <p className="text-xs text-[var(--color-text)]">{cfg?.serverUrl}</p>
        </div>
        <div className="ml-auto">
          <Badge variant={connectionState === 'connected' ? 'success' : 'danger'}>
            {connectionState}
          </Badge>
        </div>
      </Card>

      {/* Stats */}
      <div className="grid grid-cols-3 gap-4 mb-8">
        {stats.map(({ label, value, icon: Icon, accent, bg }) => (
          <Card key={label}>
            <div className={`w-9 h-9 rounded-lg ${bg} flex items-center justify-center mb-3`}>
              <Icon size={16} className={accent} />
            </div>
            <p className="text-2xl font-semibold text-[var(--color-heading)]">{value}</p>
            <p className="text-xs text-[var(--color-text)] mt-0.5">{label}</p>
          </Card>
        ))}
      </div>

      {/* Your server info */}
      <Card>
        <div className="flex items-start gap-3">
          <div className="w-9 h-9 rounded-lg bg-[var(--color-accent-dim)] flex items-center justify-center shrink-0">
            <Server size={16} className="text-[var(--color-accent)]" />
          </div>
          <div className="flex-1 min-w-0">
            <p className="text-sm font-medium text-[var(--color-heading)]">Your Server</p>
            <div className="mt-2 flex flex-col gap-1.5">
              <div className="flex gap-2 text-xs">
                <span className="text-[var(--color-text)] w-16 shrink-0">Server ID</span>
                <code className="text-[var(--color-heading)] font-mono bg-white/5 px-1.5 py-0.5 rounded truncate">
                  {cfg?.serverId}
                </code>
              </div>
              <div className="flex gap-2 text-xs items-center">
                <span className="text-[var(--color-text)] w-16 shrink-0">API Key</span>
                <code className="text-[var(--color-heading)] font-mono bg-white/5 px-1.5 py-0.5 rounded">
                  {'•'.repeat(20)}
                </code>
                <CopyButton value={cfg?.apiKey ?? ''} />
              </div>
            </div>
            <p className="text-xs text-[var(--color-text)] mt-3">
              Share your Server ID with others so they can send you invitations.
            </p>
          </div>
        </div>
      </Card>
    </div>
  )
}
