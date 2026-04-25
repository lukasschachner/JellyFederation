import * as signalR from '@microsoft/signalr'
import { useEffect, useRef, useState } from 'react'
import { useConfig } from './useConfig'

export type ConnectionState = 'disconnected' | 'connecting' | 'connected' | 'reconnecting'

export interface FileRequestUpdate {
  fileRequestId: string
  status: string
  failureReason: string | null
  selectedTransportMode?: 'ArqUdp' | 'Quic' | null
  failureCategory?: 'Timeout' | 'Connectivity' | 'Reliability' | 'Cancelled' | 'Unknown' | null
  bytesTransferred?: number | null
  totalBytes?: number | null
}

export interface TransferProgress {
  fileRequestId: string
  bytesReceived: number
  totalBytes: number
}

interface UseSignalROptions {
  onFileRequestUpdate?: (update: FileRequestUpdate) => void
  onTransferProgress?: (progress: TransferProgress) => void
}

export function useSignalR({ onFileRequestUpdate, onTransferProgress }: UseSignalROptions = {}) {
  const [state, setState] = useState<ConnectionState>('disconnected')
  const connRef = useRef<signalR.HubConnection | null>(null)
  const cfg = useConfig()

  const onFileRequestUpdateRef = useRef(onFileRequestUpdate)
  const onTransferProgressRef = useRef(onTransferProgress)

  useEffect(() => { onFileRequestUpdateRef.current = onFileRequestUpdate }, [onFileRequestUpdate])
  useEffect(() => { onTransferProgressRef.current = onTransferProgress }, [onTransferProgress])

  useEffect(() => {
    if (!cfg?.serverUrl) return

    const conn = new signalR.HubConnectionBuilder()
      .withUrl(`${cfg.serverUrl}/hubs/federation?client=web`, {
        accessTokenFactory: () => cfg.apiKey,
        withCredentials: true,
      })
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Warning)
      .build()

    conn.onreconnecting(() => setState('reconnecting'))
    conn.onreconnected(() => setState('connected'))
    conn.onclose(() => setState('disconnected'))

    conn.on('FileRequestStatusUpdate', (update: FileRequestUpdate) => {
      onFileRequestUpdateRef.current?.(update)
    })

    conn.on('TransferProgress', (progress: TransferProgress) => {
      onTransferProgressRef.current?.(progress)
    })

    conn.start()
      .then(() => setState('connected'))
      .catch(() => setState('disconnected'))

    connRef.current = conn
    return () => {
      connRef.current = null
      void conn.stop()
    }
  }, [cfg?.serverUrl, cfg?.apiKey])

  return { state }
}
