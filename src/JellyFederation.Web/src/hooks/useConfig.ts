import { useState } from 'react'
import type { Config } from '../api/types'

const KEY = 'jf_config'

export function useConfig(): Config | null {
  const [config] = useState<Config | null>(() => {
    try {
      const raw = localStorage.getItem(KEY)
      return raw ? JSON.parse(raw) : null
    } catch {
      return null
    }
  })
  return config
}
