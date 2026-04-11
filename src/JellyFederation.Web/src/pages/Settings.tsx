import { useState } from 'react'
import { LogOut, Server } from 'lucide-react'
import { loadConfig, saveConfig, clearConfig } from '../lib/config'
import { Button } from '../components/Button'
import { Card } from '../components/Card'
import { Input } from '../components/Input'

interface SettingsProps {
  onLogout: () => void
}

export function Settings({ onLogout }: SettingsProps) {
  const cfg = loadConfig()
  const [serverName, setServerName] = useState(cfg?.serverName ?? '')
  const [_downloadDir, _setDownloadDir] = useState('')
  const [saved, setSaved] = useState(false)

  function handleSave(e: React.FormEvent) {
    e.preventDefault()
    if (!cfg) return
    saveConfig({ ...cfg, serverName })
    setSaved(true)
    setTimeout(() => setSaved(false), 2000)
  }

  function handleLogout() {
    if (confirm('Remove this server from local storage? You can reconnect anytime with your API key.')) {
      clearConfig()
      onLogout()
    }
  }

  return (
    <div className="max-w-2xl">
      <div className="mb-8">
        <h1 className="text-2xl font-semibold text-[var(--color-heading)]">Settings</h1>
        <p className="text-sm text-[var(--color-text)] mt-1">Federation connection and plugin configuration</p>
      </div>

      {/* Connection */}
      <Card className="mb-6">
        <h2 className="text-sm font-semibold text-[var(--color-heading)] mb-4 flex items-center gap-2">
          <Server size={14} className="text-[var(--color-accent)]" />
          Connection
        </h2>
        <div className="flex flex-col gap-3 text-sm">
          <div className="flex items-center gap-3 py-2 border-b border-[var(--color-border)]">
            <span className="text-[var(--color-text)] w-32 shrink-0">Federation URL</span>
            <code className="text-[var(--color-heading)] font-mono text-xs">{cfg?.serverUrl}</code>
          </div>
          <div className="flex items-center gap-3 py-2 border-b border-[var(--color-border)]">
            <span className="text-[var(--color-text)] w-32 shrink-0">Server ID</span>
            <code className="text-[var(--color-heading)] font-mono text-xs break-all">{cfg?.serverId}</code>
          </div>
          <div className="flex items-center gap-3 py-2">
            <span className="text-[var(--color-text)] w-32 shrink-0">API Key</span>
            <code className="text-[var(--color-heading)] font-mono text-xs">{'•'.repeat(24)}</code>
          </div>
        </div>
      </Card>

      {/* Display settings */}
      <Card className="mb-6">
        <h2 className="text-sm font-semibold text-[var(--color-heading)] mb-4">Display</h2>
        <form onSubmit={handleSave} className="flex flex-col gap-4">
          <Input
            label="Server Name"
            value={serverName}
            onChange={e => setServerName(e.target.value)}
          />
          <div className="flex items-center gap-3">
            <Button type="submit" variant="primary">
              {saved ? '✓ Saved' : 'Save'}
            </Button>
          </div>
        </form>
      </Card>

      {/* Plugin config hint */}
      <Card className="mb-8">
        <h2 className="text-sm font-semibold text-[var(--color-heading)] mb-3">Plugin Configuration</h2>
        <p className="text-sm text-[var(--color-text)] mb-3">
          Configure these values in the Jellyfin plugin settings panel:
        </p>
        <div className="bg-black/20 rounded-lg p-4 flex flex-col gap-2 font-mono text-xs text-[var(--color-text)]">
          <div><span className="text-[var(--color-accent)]">FederationServerUrl</span> = {cfg?.serverUrl}</div>
          <div><span className="text-[var(--color-accent)]">ServerId</span> = {cfg?.serverId}</div>
          <div><span className="text-[var(--color-accent)]">ApiKey</span> = (copy from registration)</div>
          <div><span className="text-[var(--color-accent)]">DownloadDirectory</span> = /path/to/media/federation</div>
        </div>
      </Card>

      {/* Danger zone */}
      <Card className="border-red-900/40">
        <h2 className="text-sm font-semibold text-red-400 mb-2">Danger Zone</h2>
        <p className="text-sm text-[var(--color-text)] mb-4">
          Remove local credentials. Your server registration on the federation server is preserved.
        </p>
        <Button variant="danger" onClick={handleLogout}>
          <LogOut size={14} />
          Disconnect
        </Button>
      </Card>
    </div>
  )
}
