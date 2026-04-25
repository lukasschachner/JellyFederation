import { useEffect, useState } from 'react'
import type { Config } from '../api/types'
import { CONFIG_CHANGED_EVENT, CONFIG_STORAGE_KEY, loadConfig } from '../lib/config'

export function useConfig(): Config | null {
  const [config, setConfig] = useState<Config | null>(loadConfig)

  useEffect(() => {
    function refreshConfig() {
      setConfig(loadConfig())
    }

    function handleStorage(e: StorageEvent) {
      if (e.key === CONFIG_STORAGE_KEY) refreshConfig()
    }

    window.addEventListener('storage', handleStorage)
    window.addEventListener(CONFIG_CHANGED_EVENT, refreshConfig)
    return () => {
      window.removeEventListener('storage', handleStorage)
      window.removeEventListener(CONFIG_CHANGED_EVENT, refreshConfig)
    }
  }, [])

  return config
}
