import { useState } from 'react'
import { CheckCircle2, KeyRound, Server, Share2 } from 'lucide-react'
import { getErrorMessage, serversApi, sessionsApi } from '../api/client'
import { normalizeServerUrl, saveConfig } from '../lib/config'
import { Button } from '../components/Button'
import { Input } from '../components/Input'
import { Card } from '../components/Card'
import { AppLogo } from '../components/AppLogo'
import { CopyButton } from '../components/CopyButton'
import { ErrorDetails } from '../components/ErrorDetails'

interface SetupProps {
  onComplete: () => void
}

const uuidRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i

export function Setup({ onComplete }: SetupProps) {
  const [mode, setMode] = useState<'register' | 'existing'>('register')
  const [serverUrl, setServerUrl] = useState('')
  const [serverName, setServerName] = useState('')
  const [ownerUserId, setOwnerUserId] = useState('')
  const [existingServerId, setExistingServerId] = useState('')
  const [existingApiKey, setExistingApiKey] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<unknown>(null)
  const [registeredApiKey, setRegisteredApiKey] = useState<string | null>(null)

  async function handleRegister(e: React.FormEvent) {
    e.preventDefault()
    setError(null)
    setLoading(true)
    try {
      const normalizedServerUrl = normalizeServerUrl(serverUrl)
      const res = await serversApi.register(serverName.trim(), ownerUserId.trim(), normalizedServerUrl)
      await sessionsApi.create(normalizedServerUrl, res.serverId, res.apiKey)
      saveConfig({ serverUrl: normalizedServerUrl, serverId: res.serverId, apiKey: '', serverName: serverName.trim() })
      setRegisteredApiKey(res.apiKey)
    } catch (err) {
      setError(err)
    } finally {
      setLoading(false)
    }
  }

  async function handleExisting(e: React.FormEvent) {
    e.preventDefault()
    const normalizedServerUrl = normalizeServerUrl(serverUrl)
    const serverId = existingServerId.trim()
    const apiKey = existingApiKey.trim()

    if (!normalizedServerUrl || !serverId || !apiKey) {
      setError(new Error('All fields are required.'))
      return
    }
    if (!uuidRegex.test(serverId)) {
      setError(new Error('Server ID must be a valid UUID.'))
      return
    }

    setError(null)
    setLoading(true)
    try {
      const server = await serversApi.verify(normalizedServerUrl, serverId, apiKey)
      await sessionsApi.create(normalizedServerUrl, serverId, apiKey)
      saveConfig({ serverUrl: normalizedServerUrl, serverId, apiKey: '', serverName: server.name || 'My Server' })
      onComplete()
    } catch (err) {
      setError(err)
    } finally {
      setLoading(false)
    }
  }

  if (registeredApiKey) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-[radial-gradient(circle_at_top_left,rgba(129,140,248,.12),transparent_34rem),var(--color-bg)] p-6">
        <div className="w-full max-w-lg">
          <Card className="bg-gradient-to-b from-[var(--color-accent-dim)] to-transparent">
            <div className="mb-5 flex items-start gap-3">
              <div className="flex h-11 w-11 items-center justify-center rounded-2xl bg-emerald-500/15 text-emerald-400">
                <CheckCircle2 size={22} />
              </div>
              <div>
                <h1 className="text-2xl font-semibold tracking-tight text-[var(--color-heading)]">Server registered</h1>
                <p className="mt-1 text-sm leading-6 text-[var(--color-text)]">
                  Copy this API key into your Jellyfin plugin settings now. For security, it will not be stored in browser local storage.
                </p>
              </div>
            </div>
            <code className="block rounded-xl border border-[var(--color-border)] bg-black/30 p-3 text-xs text-[var(--color-heading)] break-all">
              {registeredApiKey}
            </code>
            <div className="mt-4 flex flex-col gap-2 sm:flex-row">
              <CopyButton value={registeredApiKey} label="Copy API Key" successMessage="API key copied" className="h-10" />
              <Button type="button" variant="primary" onClick={onComplete} className="justify-center">
                Continue
              </Button>
            </div>
          </Card>
        </div>
      </div>
    )
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-[radial-gradient(circle_at_top_left,rgba(129,140,248,.12),transparent_34rem),var(--color-bg)] p-6">
      <div className="grid w-full max-w-5xl gap-6 lg:grid-cols-[minmax(0,1fr)_420px] lg:items-center">
        <div className="hidden lg:block">
          <div className="mb-5 inline-flex items-center gap-2 rounded-full border border-[var(--color-accent)]/25 bg-[var(--color-accent-dim)] px-3 py-1 text-xs font-medium text-[var(--color-accent)]">
            <Share2 size={13} />
            Federated Jellyfin libraries
          </div>
          <div className="mb-6 flex items-center gap-4">
            <AppLogo size="lg" />
            <div>
              <p className="text-sm font-medium text-[var(--color-accent)]">JellyFederation</p>
              <h1 className="max-w-xl text-4xl font-semibold tracking-tight text-[var(--color-heading)]">Connect your Jellyfin server to the federation.</h1>
            </div>
          </div>
          <p className="mt-4 max-w-xl text-sm leading-7 text-[var(--color-text)]">
            Register a new server or reconnect an existing one. You’ll use the generated API key in the Jellyfin plugin settings.
          </p>
          <div className="mt-8 grid max-w-xl gap-3 sm:grid-cols-2">
            <Card variant="muted"><Server size={18} className="mb-3 text-[var(--color-accent)]" /><p className="text-sm font-medium text-[var(--color-heading)]">Server identity</p><p className="mt-1 text-xs leading-5 text-[var(--color-text)]">A stable ID identifies your library to trusted peers.</p></Card>
            <Card variant="muted"><KeyRound size={18} className="mb-3 text-[var(--color-accent)]" /><p className="text-sm font-medium text-[var(--color-heading)]">Private API key</p><p className="mt-1 text-xs leading-5 text-[var(--color-text)]">Keep your key secret and paste it into the plugin.</p></Card>
          </div>
        </div>

        <div>
          <div className="mb-6 flex items-center gap-3 lg:hidden">
            <AppLogo size="md" />
            <div>
              <h1 className="text-xl font-semibold text-[var(--color-heading)]">JellyFederation</h1>
              <p className="text-sm text-[var(--color-text)]">Connect your Jellyfin server</p>
            </div>
          </div>

          <Card className="bg-gradient-to-b from-white/[0.03] to-transparent">
            <div className="mb-6 grid grid-cols-2 gap-2 rounded-xl bg-black/15 p-1">
              {(['register', 'existing'] as const).map(m => (
                <button
                  key={m}
                  type="button"
                  aria-pressed={mode === m}
                  onClick={() => { setMode(m); setError(null) }}
                  className={`rounded-lg py-2 text-sm font-medium transition-colors cursor-pointer ${mode === m ? 'bg-[var(--color-accent-dim)] text-[var(--color-accent)]' : 'text-[var(--color-text)] hover:bg-white/5'}`}
                >
                  {m === 'register' ? 'New Server' : 'Existing Server'}
                </button>
              ))}
            </div>

            {mode === 'register' ? (
              <form onSubmit={handleRegister} className="flex flex-col gap-4">
                <Input label="Federation Server URL" placeholder="https://federation.example.com" value={serverUrl} onChange={e => setServerUrl(e.target.value)} required />
                <Input label="Server Name" placeholder="Living Room Jellyfin" value={serverName} onChange={e => setServerName(e.target.value)} required />
                <Input label="Your User ID" placeholder="A unique identifier for you" value={ownerUserId} onChange={e => setOwnerUserId(e.target.value)} hint="Used to identify your server in the federation network" required />
                {error !== null && <ErrorDetails error={error} title="Registration failed" />}
                <Button type="submit" variant="primary" loading={loading} className="mt-1 justify-center">Register Server</Button>
              </form>
            ) : (
              <form onSubmit={handleExisting} className="flex flex-col gap-4">
                <Input label="Federation Server URL" placeholder="https://federation.example.com" value={serverUrl} onChange={e => setServerUrl(e.target.value)} required />
                <Input label="Server ID" placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" value={existingServerId} onChange={e => setExistingServerId(e.target.value)} error={existingServerId && !uuidRegex.test(existingServerId.trim()) ? 'Enter a valid UUID' : undefined} required />
                <Input label="API Key" type="password" placeholder="Your server API key" value={existingApiKey} onChange={e => setExistingApiKey(e.target.value)} required />
                {error !== null && <ErrorDetails error={error} title={getErrorMessage(error, 'Connection failed')} />}
                <Button type="submit" variant="primary" loading={loading} className="mt-1 justify-center">Connect</Button>
              </form>
            )}
          </Card>
        </div>
      </div>
    </div>
  )
}
