import { loadConfig, normalizeServerUrl } from '../lib/config'
import type { Config, FileRequest, Invitation, MediaItem, ServerInfo } from './types'

interface BackendErrorEnvelope {
  error?: {
    code?: string
    category?: string
    message?: string
    correlationId?: string
    details?: unknown
  }
}

export class ApiClientError extends Error {
  readonly status: number
  readonly statusText: string
  readonly code?: string
  readonly category?: string
  readonly correlationId?: string
  readonly details?: unknown
  readonly rawBody?: string

  constructor({
    message,
    status,
    statusText,
    code,
    category,
    correlationId,
    details,
    rawBody,
  }: {
    message: string
    status: number
    statusText: string
    code?: string
    category?: string
    correlationId?: string
    details?: unknown
    rawBody?: string
  }) {
    super(message)
    this.name = 'ApiClientError'
    this.status = status
    this.statusText = statusText
    this.code = code
    this.category = category
    this.correlationId = correlationId
    this.details = details
    this.rawBody = rawBody
  }
}

function getConfig(): Config | null {
  return loadConfig()
}

function headers(): Record<string, string> {
  const cfg = getConfig()
  return {
    'Content-Type': 'application/json',
    ...(cfg?.apiKey ? { 'X-Api-Key': cfg.apiKey } : {}),
  }
}

function base(): string {
  return getConfig()?.serverUrl ?? ''
}

function tryParseBackendError(text: string): BackendErrorEnvelope | null {
  if (!text.trim()) return null
  try {
    const parsed = JSON.parse(text) as unknown
    if (typeof parsed === 'object' && parsed !== null && 'error' in parsed) {
      return parsed as BackendErrorEnvelope
    }
  } catch {
    return null
  }
  return null
}

async function createApiError(res: Response): Promise<ApiClientError> {
  const text = await res.text().catch(() => '')
  const backend = tryParseBackendError(text)
  const backendError = backend?.error
  const message = backendError?.message?.trim()
    || text.trim()
    || res.statusText
    || `HTTP ${res.status}`

  return new ApiClientError({
    message,
    status: res.status,
    statusText: res.statusText,
    code: backendError?.code,
    category: backendError?.category,
    correlationId: backendError?.correlationId,
    details: backendError?.details,
    rawBody: text || undefined,
  })
}

export function getErrorMessage(err: unknown, fallback = 'Request failed'): string {
  return err instanceof Error && err.message ? err.message : fallback
}

export function getErrorMetadata(err: unknown): string | undefined {
  if (!(err instanceof ApiClientError)) return undefined

  const parts = [
    err.category ? `Category: ${err.category}` : undefined,
    err.code ? `Code: ${err.code}` : undefined,
    err.correlationId ? `Correlation ID: ${err.correlationId}` : undefined,
  ].filter(Boolean)

  return parts.length > 0 ? parts.join('\n') : undefined
}

export function getErrorDescription(err: unknown, fallback = 'Request failed'): string {
  const message = getErrorMessage(err, fallback)
  const metadata = getErrorMetadata(err)
  return metadata ? `${message}\n\n${metadata}` : message
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let res: Response
  try {
    res = await fetch(`${base()}${path}`, {
      ...init,
      credentials: 'include',
      headers: { ...headers(), ...init?.headers },
    })
  } catch {
    throw new Error('Cannot reach the federation server. Is it running and is the URL correct?')
  }
  if (!res.ok) {
    if (res.status === 401) {
      if (typeof window !== 'undefined') {
        window.dispatchEvent(new Event('jf-auth-invalid'))
      }
      throw new Error('Unauthorized: your local API key is missing or no longer valid. Reconnect in Setup.')
    }
    throw await createApiError(res)
  }
  if (res.status === 204) return undefined as T
  return res.json()
}

async function requestAbsolute<T>(url: string, init?: RequestInit): Promise<T> {
  let res: Response
  try {
    res = await fetch(url, {
      ...init,
      credentials: 'include',
      headers: { ...init?.headers },
    })
  } catch {
    throw new Error('Cannot reach the federation server. Is it running and is the URL correct?')
  }

  if (!res.ok) {
    if (res.status === 401) {
      throw new Error('Unauthorized: the API key is missing or no longer valid.')
    }
    throw await createApiError(res)
  }

  return res.json() as Promise<T>
}

function normalizeFileRequest(raw: FileRequest): FileRequest {
  return {
    ...raw,
    selectedTransportMode: raw.selectedTransportMode ?? null,
    failureCategory: raw.failureCategory ?? null,
    bytesTransferred: raw.bytesTransferred ?? 0,
    totalBytes: raw.totalBytes ?? null,
  }
}

export const sessionsApi = {
  create: async (serverUrl: string, serverId: string, apiKey: string) =>
    requestAbsolute<{ serverId: string; serverName: string }>(`${normalizeServerUrl(serverUrl)}/api/sessions`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ serverId, apiKey }),
    }),

  delete: async () => {
    await request<void>('/api/sessions', { method: 'DELETE' })
  },
}

// Servers
export const serversApi = {
  register: async (name: string, ownerUserId: string, serverUrl: string) =>
    requestAbsolute<{ serverId: string; apiKey: string }>(`${normalizeServerUrl(serverUrl)}/api/servers/register`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ name, ownerUserId }),
    }),

  verify: async (serverUrl: string, serverId: string, apiKey: string) =>
    requestAbsolute<ServerInfo>(`${normalizeServerUrl(serverUrl)}/api/servers/${serverId}`, {
      headers: { 'X-Api-Key': apiKey },
    }),

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
  list: async () => (await request<FileRequest[]>('/api/filerequests')).map(normalizeFileRequest),
  create: (jellyfinItemId: string, owningServerId: string) =>
    request<FileRequest>('/api/filerequests', {
      method: 'POST',
      body: JSON.stringify({ jellyfinItemId, owningServerId }),
    }).then(normalizeFileRequest),
  cancel: (id: string) =>
    request<void>(`/api/filerequests/${id}/cancel`, { method: 'PUT' }),
}
