import { useState, useEffect } from 'react'
import type { Config } from '../api/types'

const KEY = 'jf_config'

function readConfig(): Config | null {
  try {
    const raw = localStorage.getItem(KEY)
    return raw ? JSON.parse(raw) : null
  } catch {
    return null
  }
}

export function useConfig(): Config | null {
  const [config, setConfig] = useState<Config | null>(readConfig)

  useEffect(() => {
    function handleStorage(e: StorageEvent) {
      if (e.key !== KEY) return
      try {
        setConfig(e.newValue ? JSON.parse(e.newValue) : null)
      } catch {
        setConfig(null)
      }
    }
    window.addEventListener('storage', handleStorage)
    return () => window.removeEventListener('storage', handleStorage)
  }, [])

  return config
}
