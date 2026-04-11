import type { Config, FileRequest, Invitation, MediaItem, ServerInfo } from './types'

function getConfig(): Config | null {
  const raw = localStorage.getItem('jf_config')
  return raw ? JSON.parse(raw) : null
}

function headers(): Record<string, string> {
  const cfg = getConfig()
  return {
    'Content-Type': 'application/json',
    ...(cfg?.apiKey ? { 'X-Api-Key': cfg.apiKey } : {}),
  }
}

function base(): string {
  return getConfig()?.serverUrl?.replace(/\/$/, '') ?? ''
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let res: Response
  try {
    res = await fetch(`${base()}${path}`, { ...init, headers: { ...headers(), ...init?.headers } })
  } catch {
    throw new Error('Cannot reach the federation server. Is it running and is the URL correct?')
  }
  if (!res.ok) {
    const text = await res.text().catch(() => res.statusText)
    throw new Error(text || `HTTP ${res.status}`)
  }
  if (res.status === 204) return undefined as T
  return res.json()
}

// Servers
export const serversApi = {
  register: async (name: string, ownerUserId: string, serverUrl: string) => {
    let res: Response
    try {
      res = await fetch(`${serverUrl.replace(/\/$/, '')}/api/servers/register`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name, ownerUserId }),
      })
    } catch {
      throw new Error('Cannot reach the federation server. Is it running and is the URL correct?')
    }
    if (!res.ok) {
      const text = await res.text().catch(() => res.statusText)
      throw new Error(text || `HTTP ${res.status}`)
    }
    return res.json() as Promise<{ serverId: string; apiKey: string }>
  },

  list: () => request<ServerInfo[]>('/api/servers'),
  get: (id: string) => request<ServerInfo>(`/api/servers/${id}`),
}

// Library
export const libraryApi = {
  browse: (search?: string, type?: string, page = 1, pageSize = 100) => {
    const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) })
    if (search) params.set('search', search)
    if (type && type !== 'All') params.set('type', type)
    return request<MediaItem[]>(`/api/library?${params}`)
  },
  browseCounts: (search?: string) => {
    const params = new URLSearchParams()
    if (search) params.set('search', search)
    return request<Record<string, number>>(`/api/library/counts?${params}`)
  },
  mine: (search?: string, type?: string, page = 1, pageSize = 100) => {
    const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) })
    if (search) params.set('search', search)
    if (type && type !== 'All') params.set('type', type)
    return request<MediaItem[]>(`/api/library/mine?${params}`)
  },
  mineCounts: (search?: string) => {
    const params = new URLSearchParams()
    if (search) params.set('search', search)
    return request<Record<string, number>>(`/api/library/mine/counts?${params}`)
  },
  setRequestable: (itemId: string, isRequestable: boolean) =>
    request<void>(`/api/library/${itemId}/requestable`, {
      method: 'PUT',
      body: JSON.stringify({ isRequestable }),
    }),
}

// Invitations
export const invitationsApi = {
  list: () => request<Invitation[]>('/api/invitations'),
  send: (toServerId: string) =>
    request<Invitation>('/api/invitations', {
      method: 'POST',
      body: JSON.stringify({ toServerId }),
    }),
  respond: (id: string, accept: boolean) =>
    request<Invitation>(`/api/invitations/${id}/respond`, {
      method: 'PUT',
      body: JSON.stringify({ accept }),
    }),
  revoke: (id: string) =>
    request<void>(`/api/invitations/${id}`, { method: 'DELETE' }),
}

// File requests
export const fileRequestsApi = {
  list: () => request<FileRequest[]>('/api/filerequests'),
  create: (jellyfinItemId: string, owningServerId: string) =>
    request<FileRequest>('/api/filerequests', {
      method: 'POST',
      body: JSON.stringify({ jellyfinItemId, owningServerId }),
    }),
  cancel: (id: string) =>
    request<void>(`/api/filerequests/${id}/cancel`, { method: 'PUT' }),
}
