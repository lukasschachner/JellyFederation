import type { Config } from '../api/types'

const KEY = 'jf_config'

export function loadConfig(): Config | null {
  const raw = localStorage.getItem(KEY)
  return raw ? JSON.parse(raw) : null
}

export function saveConfig(cfg: Config) {
  localStorage.setItem(KEY, JSON.stringify(cfg))
}

export function clearConfig() {
  localStorage.removeItem(KEY)
}
