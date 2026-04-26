import { useState } from 'react'
import { Check, ExternalLink, KeyRound, Link2, LogOut, PlugZap, Server, ShieldCheck, UserRound } from 'lucide-react'
import { sessionsApi } from '../api/client'
import { saveConfig, clearConfig } from '../lib/config'
import { Button } from '../components/Button'
import { Card } from '../components/Card'
import { ConfirmDialog } from '../components/ConfirmDialog'
import { CopyButton } from '../components/CopyButton'
import { Input } from '../components/Input'
import { useToast } from '../hooks/useToast'
import { useConfig } from '../hooks/useConfig'

interface SettingsProps {
  onLogout: () => void
}

interface DetailRowProps {
  label: string
  value: string | undefined
  displayValue?: string
  copyLabel?: string
  copyMessage?: string
  icon?: React.ReactNode
  sensitive?: boolean
}

function DetailRow({ label, value, displayValue, copyLabel, copyMessage, icon, sensitive }: DetailRowProps) {
  const shown = displayValue ?? value ?? 'Not configured'

  return (
    <div className="group grid min-w-0 gap-2 border-b border-[var(--color-border)]/80 py-4 last:border-b-0 sm:grid-cols-[180px_minmax(0,1fr)_auto] sm:items-center sm:gap-4">
      <div className="flex min-w-0 items-center gap-2 text-sm text-[var(--color-text)]">
        {icon && <span className="shrink-0 text-[var(--color-accent)]">{icon}</span>}
        <span className="truncate">{label}</span>
      </div>
      <code className={`block min-w-0 max-w-full overflow-hidden truncate whitespace-nowrap rounded-lg bg-black/15 px-3 py-2 font-mono text-xs text-[var(--color-heading)] ring-1 ring-white/5 ${sensitive ? 'tracking-[0.18em]' : ''}`}>
        {shown}
      </code>
      <CopyButton
        value={value}
        label={copyLabel ?? `Copy ${label}`}
        successMessage={copyMessage ?? `${label} copied`}
        className="justify-self-start whitespace-nowrap sm:justify-self-end"
      />
    </div>
  )
}

function ConfigLine({ name, value }: { name: string; value: string }) {
  return (
    <div className="flex min-w-0 items-center gap-2 rounded-lg px-2 py-1.5 hover:bg-white/[0.03]">
      <span className="shrink-0 text-[var(--color-accent)]">{name}</span>
      <span className="text-[var(--color-text)]">=</span>
      <span className="min-w-0 truncate text-[var(--color-heading)]">{value}</span>
    </div>
  )
}

export function Settings({ onLogout }: SettingsProps) {
  const cfg = useConfig()
  const toast = useToast()
  const [serverName, setServerName] = useState(cfg?.serverName ?? '')
  const [saved, setSaved] = useState(false)
  const [confirmDisconnectOpen, setConfirmDisconnectOpen] = useState(false)

  const hasDisplayChanges = serverName.trim() !== (cfg?.serverName ?? '')
  const apiKeyDisplay = cfg?.apiKey ? '•'.repeat(24) : 'Stored in secure browser session'
  const pluginConfig = [
    ['FederationServerUrl', cfg?.serverUrl ?? ''],
    ['ServerId', cfg?.serverId ?? ''],
    ['ApiKey', cfg?.apiKey ?? '<copy from registration or reconnect with your API key>'],
    ['DownloadDirectory', '/path/to/media/federation'],
  ] as const
  const pluginConfigText = pluginConfig.map(([key, value]) => `${key}=${value}`).join('\n')

  function handleSave(e: React.FormEvent) {
    e.preventDefault()
    if (!cfg) return
    if (!hasDisplayChanges) return
    saveConfig({ ...cfg, serverName: serverName.trim() })
    setSaved(true)
    toast.success('Settings saved')
    setTimeout(() => setSaved(false), 2000)
  }

  async function handleLogout() {
    await sessionsApi.delete().catch(() => undefined)
    clearConfig()
    setConfirmDisconnectOpen(false)
    onLogout()
  }

  return (
    <div className="max-w-4xl">
      <div className="mb-8 overflow-hidden rounded-2xl border border-[var(--color-border)] bg-gradient-to-br from-[var(--color-surface)] via-[var(--color-surface)] to-[var(--color-accent-dim)] p-6 shadow-xl shadow-black/20">
        <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <div className="mb-3 inline-flex items-center gap-2 rounded-full border border-[var(--color-accent)]/25 bg-[var(--color-accent-dim)] px-3 py-1 text-xs font-medium text-[var(--color-accent)]">
              <ShieldCheck size={13} />
              Local browser connection
            </div>
            <h1 className="text-3xl font-semibold tracking-tight text-[var(--color-heading)]">Settings</h1>
            <p className="mt-2 max-w-2xl text-sm leading-6 text-[var(--color-text)]">
              Manage this web client’s federation connection, copy plugin configuration values, and update display preferences.
            </p>
          </div>
          <div className="rounded-xl border border-[var(--color-border)] bg-black/15 px-4 py-3 text-sm">
            <p className="text-xs uppercase tracking-wide text-[var(--color-text)]">Server</p>
            <p className="mt-1 max-w-[220px] truncate font-medium text-[var(--color-heading)]">{cfg?.serverName ?? 'Unknown server'}</p>
          </div>
        </div>
      </div>

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_320px]">
        <div className="space-y-6">
          <Card className="overflow-hidden bg-gradient-to-b from-white/[0.03] to-transparent p-0 shadow-lg shadow-black/10">
            <div className="border-b border-[var(--color-border)]/80 px-5 py-4">
              <h2 className="flex items-center gap-2 text-sm font-semibold text-[var(--color-heading)]">
                <Server size={15} className="text-[var(--color-accent)]" />
                Connection details
              </h2>
              <p className="mt-1 text-xs text-[var(--color-text)]">Use the copy buttons to paste exact values into clients or diagnostics.</p>
            </div>
            <div className="px-5">
              <DetailRow
                label="Federation URL"
                value={cfg?.serverUrl}
                icon={<Link2 size={14} />}
                copyMessage="Federation URL copied"
              />
              <DetailRow
                label="Server ID"
                value={cfg?.serverId}
                icon={<Server size={14} />}
                copyMessage="Server ID copied"
              />
              <DetailRow
                label="API Key"
                value={cfg?.apiKey}
                displayValue={apiKeyDisplay}
                icon={<KeyRound size={14} />}
                sensitive
                copyMessage="API key copied"
              />
            </div>
          </Card>

          <Card className="bg-gradient-to-b from-white/[0.03] to-transparent shadow-lg shadow-black/10">
            <h2 className="mb-4 flex items-center gap-2 text-sm font-semibold text-[var(--color-heading)]">
              <UserRound size={15} className="text-[var(--color-accent)]" />
              Display
            </h2>
            <form onSubmit={handleSave} className="flex flex-col gap-4">
              <Input
                label="Server Name"
                value={serverName}
                onChange={e => setServerName(e.target.value)}
              />
              <div className="flex items-center gap-3">
                <Button type="submit" variant="primary" disabled={!hasDisplayChanges}>
                  {saved ? <><Check size={14} /> Saved</> : hasDisplayChanges ? 'Save changes' : 'No changes'}
                </Button>
                <span className="text-xs text-[var(--color-text)]">
                  {hasDisplayChanges ? 'You have unsaved display changes.' : 'Shown in this browser only.'}
                </span>
              </div>
            </form>
          </Card>
        </div>

        <div className="space-y-6">
          <Card className="bg-gradient-to-b from-[var(--color-accent-dim)] to-transparent shadow-lg shadow-black/10">
            <div className="mb-4 flex items-start justify-between gap-3">
              <div>
                <h2 className="flex items-center gap-2 text-sm font-semibold text-[var(--color-heading)]">
                  <PlugZap size={15} className="text-[var(--color-accent)]" />
                  Plugin configuration
                </h2>
                <p className="mt-1 text-xs leading-5 text-[var(--color-text)]">Paste these into the Jellyfin plugin settings panel.</p>
              </div>
              <CopyButton value={pluginConfigText} label="Copy all" successMessage="Plugin configuration copied" />
            </div>

            <div className="rounded-xl border border-[var(--color-border)] bg-black/20 p-2 font-mono text-xs">
              {pluginConfig.map(([name, value]) => (
                <ConfigLine key={name} name={name} value={value} />
              ))}
            </div>

            <a
              href="/"
              className="mt-4 inline-flex items-center gap-1.5 text-xs font-medium text-[var(--color-accent)] hover:text-[var(--color-heading)]"
            >
              Open dashboard
              <ExternalLink size={12} />
            </a>
          </Card>

          <Card className="border-red-900/40 bg-gradient-to-b from-red-950/20 to-transparent shadow-lg shadow-black/10">
            <h2 className="mb-2 text-sm font-semibold text-red-400">Danger Zone</h2>
            <p className="mb-4 text-sm leading-6 text-[var(--color-text)]">
              Remove local credentials. Your server registration on the federation server is preserved.
            </p>
            <Button variant="danger" onClick={() => setConfirmDisconnectOpen(true)}>
              <LogOut size={14} />
              Disconnect
            </Button>
          </Card>
        </div>
      </div>

      <ConfirmDialog
        open={confirmDisconnectOpen}
        title="Disconnect this browser?"
        description="This removes local credentials from this browser. Your server registration on the federation server is preserved."
        confirmLabel="Disconnect"
        variant="danger"
        onConfirm={handleLogout}
        onCancel={() => setConfirmDisconnectOpen(false)}
      />
    </div>
  )
}
