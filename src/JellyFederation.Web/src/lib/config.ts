import type { Config } from '../api/types'

const KEY = 'jf_config'

export function loadConfig(): Config | null {
  try {
    const raw = localStorage.getItem(KEY)
    return raw ? JSON.parse(raw) : null
  } catch {
    return null
  }
}

export function saveConfig(cfg: Config) {
  localStorage.setItem(KEY, JSON.stringify(cfg))
}

export function clearConfig() {
  localStorage.removeItem(KEY)
}
