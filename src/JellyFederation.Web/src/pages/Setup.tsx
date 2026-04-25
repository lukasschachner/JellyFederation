import { useState } from 'react'
import { Share2 } from 'lucide-react'
import { serversApi, sessionsApi } from '../api/client'
import { normalizeServerUrl, saveConfig } from '../lib/config'
import { Button } from '../components/Button'
import { Input } from '../components/Input'
import { Card } from '../components/Card'

interface SetupProps {
  onComplete: () => void
}

export function Setup({ onComplete }: SetupProps) {
  const [mode, setMode] = useState<'register' | 'existing'>('register')
  const [serverUrl, setServerUrl] = useState('')
  const [serverName, setServerName] = useState('')
  const [ownerUserId, setOwnerUserId] = useState('')
  const [existingServerId, setExistingServerId] = useState('')
  const [existingApiKey, setExistingApiKey] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [registeredApiKey, setRegisteredApiKey] = useState<string | null>(null)

  async function handleRegister(e: React.FormEvent) {
    e.preventDefault()
    setError('')
    setLoading(true)
    try {
      const normalizedServerUrl = normalizeServerUrl(serverUrl)
      const res = await serversApi.register(serverName, ownerUserId, normalizedServerUrl)
      await sessionsApi.create(normalizedServerUrl, res.serverId, res.apiKey)
      saveConfig({
        serverUrl: normalizedServerUrl,
        serverId: res.serverId,
        apiKey: '',
        serverName,
      })
      setRegisteredApiKey(res.apiKey)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Registration failed')
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
      setError('All fields are required')
      return
    }

    setError('')
    setLoading(true)
    try {
      const server = await serversApi.verify(normalizedServerUrl, serverId, apiKey)
      await sessionsApi.create(normalizedServerUrl, serverId, apiKey)
      saveConfig({
        serverUrl: normalizedServerUrl,
        serverId,
        apiKey: '',
        serverName: server.name || 'My Server',
      })
      onComplete()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Connection failed')
    } finally {
      setLoading(false)
    }
  }

  if (registeredApiKey) {
    return (
      <div className="min-h-screen flex items-center justify-center p-6">
        <div className="w-full max-w-md">
          <Card className="flex flex-col gap-4">
            <div>
              <h1 className="text-xl font-semibold text-[var(--color-heading)]">Server registered</h1>
              <p className="text-sm text-[var(--color-text)] mt-1">
                Copy this API key into your Jellyfin plugin settings now. For security, it will not be stored in browser local storage.
              </p>
            </div>
            <code className="block bg-black/30 border border-[var(--color-border)] rounded-lg p-3 text-xs text-[var(--color-heading)] break-all">
              {registeredApiKey}
            </code>
            <Button
              type="button"
              variant="secondary"
              onClick={() => void navigator.clipboard.writeText(registeredApiKey)}
            >
              Copy API Key
            </Button>
            <Button type="button" variant="primary" onClick={onComplete}>
              Continue
            </Button>
          </Card>
        </div>
      </div>
    )
  }

  return (
    <div className="min-h-screen flex items-center justify-center p-6">
      <div className="w-full max-w-md">
        <div className="flex items-center gap-3 mb-8">
          <div className="w-10 h-10 rounded-xl bg-[var(--color-accent-dim)] flex items-center justify-center">
            <Share2 size={20} className="text-[var(--color-accent)]" />
          </div>
          <div>
            <h1 className="text-xl font-semibold text-[var(--color-heading)]">JellyFederation</h1>
            <p className="text-sm text-[var(--color-text)]">Connect your Jellyfin server to the federation</p>
          </div>
        </div>

        <Card>
          <div className="flex gap-2 mb-6">
            {(['register', 'existing'] as const).map(m => (
              <button
                key={m}
                type="button"
                aria-pressed={mode === m}
                onClick={() => setMode(m)}
                className={`flex-1 py-1.5 rounded-lg text-sm font-medium transition-colors cursor-pointer ${
                  mode === m
                    ? 'bg-[var(--color-accent-dim)] text-[var(--color-accent)]'
                    : 'text-[var(--color-text)] hover:bg-white/5'
                }`}
              >
                {m === 'register' ? 'New Server' : 'Existing Server'}
              </button>
            ))}
          </div>

          {mode === 'register' ? (
            <form onSubmit={handleRegister} className="flex flex-col gap-4">
              <Input
                label="Federation Server URL"
                placeholder="https://federation.example.com"
                value={serverUrl}
                onChange={e => setServerUrl(e.target.value)}
                required
              />
              <Input
                label="Server Name"
                placeholder="My Plex — oh wait, Jellyfin"
                value={serverName}
                onChange={e => setServerName(e.target.value)}
                required
              />
              <Input
                label="Your User ID"
                placeholder="A unique identifier for you"
                value={ownerUserId}
                onChange={e => setOwnerUserId(e.target.value)}
                hint="Used to identify your server in the federation network"
                required
              />
              {error && <p className="text-sm text-red-400">{error}</p>}
              <Button type="submit" variant="primary" loading={loading} className="mt-1">
                Register Server
              </Button>
            </form>
          ) : (
            <form onSubmit={handleExisting} className="flex flex-col gap-4">
              <Input
                label="Federation Server URL"
                placeholder="https://federation.example.com"
                value={serverUrl}
                onChange={e => setServerUrl(e.target.value)}
                required
              />
              <Input
                label="Server ID"
                placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
                value={existingServerId}
                onChange={e => setExistingServerId(e.target.value)}
                required
              />
              <Input
                label="API Key"
                type="password"
                placeholder="Your server API key"
                value={existingApiKey}
                onChange={e => setExistingApiKey(e.target.value)}
                required
              />
              {error && <p className="text-sm text-red-400">{error}</p>}
              <Button type="submit" variant="primary" loading={loading} className="mt-1">
                Connect
              </Button>
            </form>
          )}
        </Card>
      </div>
    </div>
  )
}
