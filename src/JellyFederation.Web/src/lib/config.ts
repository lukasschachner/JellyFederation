import type { Config } from '../api/types'

export const CONFIG_STORAGE_KEY = 'jf_config'
export const CONFIG_CHANGED_EVENT = 'jf-config-changed'

function isConfig(value: unknown): value is Config {
  if (typeof value !== 'object' || value === null) return false

  const candidate = value as Record<string, unknown>
  return typeof candidate.serverUrl === 'string'
    && typeof candidate.serverId === 'string'
    && typeof candidate.apiKey === 'string'
    && typeof candidate.serverName === 'string'
}

export function normalizeServerUrl(serverUrl: string): string {
  return serverUrl.trim().replace(/\/+$/, '')
}

export function loadConfig(): Config | null {
  try {
    const raw = localStorage.getItem(CONFIG_STORAGE_KEY)
    if (!raw) return null

    const parsed: unknown = JSON.parse(raw)
    return isConfig(parsed)
      ? { ...parsed, serverUrl: normalizeServerUrl(parsed.serverUrl) }
      : null
  } catch {
    return null
  }
}

export function saveConfig(cfg: Config) {
  localStorage.setItem(CONFIG_STORAGE_KEY, JSON.stringify({
    ...cfg,
    serverUrl: normalizeServerUrl(cfg.serverUrl),
  }))
  window.dispatchEvent(new Event(CONFIG_CHANGED_EVENT))
}

export function clearConfig() {
  localStorage.removeItem(CONFIG_STORAGE_KEY)
  window.dispatchEvent(new Event(CONFIG_CHANGED_EVENT))
}
