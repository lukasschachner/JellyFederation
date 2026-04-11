export type MediaType = 'Movie' | 'Series' | 'Episode' | 'Music' | 'Other'
export type InvitationStatus = 'Pending' | 'Accepted' | 'Declined' | 'Revoked'
export type FileRequestStatus = 'Pending' | 'HolePunching' | 'Transferring' | 'Completed' | 'Failed' | 'Cancelled'

export interface ServerInfo {
  id: string
  name: string
  ownerUserId: string
  isOnline: boolean
  lastSeenAt: string
  mediaItemCount: number
}

export interface MediaItem {
  id: string
  serverId: string
  serverName: string
  jellyfinItemId: string
  title: string
  type: MediaType
  year: number | null
  overview: string | null
  imageUrl: string | null
  fileSizeBytes: number
  isRequestable: boolean
}

export interface Invitation {
  id: string
  fromServerId: string
  fromServerName: string
  toServerId: string
  toServerName: string
  status: InvitationStatus
  createdAt: string
}

export interface FileRequest {
  id: string
  requestingServerId: string
  requestingServerName: string
  owningServerId: string
  owningServerName: string
  jellyfinItemId: string
  itemTitle: string | null
  status: FileRequestStatus
  failureReason: string | null
  createdAt: string
}

export interface Config {
  serverUrl: string
  serverId: string
  apiKey: string
  serverName: string
}
