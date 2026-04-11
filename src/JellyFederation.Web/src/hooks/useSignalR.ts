import * as signalR from '@microsoft/signalr'
import { useEffect, useRef, useState } from 'react'
import { useConfig } from './useConfig'

export type ConnectionState = 'disconnected' | 'connecting' | 'connected' | 'reconnecting'

export interface FileRequestUpdate {
  fileRequestId: string
  status: string
  failureReason: string | null
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
    if (!cfg?.serverUrl || !cfg?.apiKey) return

    const conn = new signalR.HubConnectionBuilder()
      .withUrl(`${cfg.serverUrl.replace(/\/$/, '')}/hubs/federation?apiKey=${cfg.apiKey}&client=web`)
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

    setState('connecting')
    conn.start()
      .then(() => setState('connected'))
      .catch(() => setState('disconnected'))

    connRef.current = conn
    return () => { conn.stop() }
  }, [cfg?.serverUrl, cfg?.apiKey])

  return { state }
}
