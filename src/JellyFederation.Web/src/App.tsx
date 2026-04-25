import { useEffect, useState } from 'react'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { QueryClient, QueryClientProvider, useQueryClient } from '@tanstack/react-query'
import { loadConfig } from './lib/config'
import { useSignalR } from './hooks/useSignalR'
import { requestsLiveUpdatedAtQueryKey, requestsQueryKey, transferProgressQueryKey } from './api/queryKeys'
import type { FileRequest, FileRequestStatus, TransferFailureCategory, TransferTransportMode } from './api/types'
import type { FileRequestUpdate, TransferProgress } from './hooks/useSignalR'
import { Layout } from './components/Layout'
import { Setup } from './pages/Setup'
import { Dashboard } from './pages/Dashboard'
import { Library } from './pages/Library'
import { Invitations } from './pages/Invitations'
import { Requests } from './pages/Requests'
import { MyMedia } from './pages/MyMedia'
import { Settings } from './pages/Settings'

const queryClient = new QueryClient({
  defaultOptions: { queries: { staleTime: 30_000, retry: 1 } },
})

function applyFileRequestUpdate(request: FileRequest, update: FileRequestUpdate): FileRequest {
  return {
    ...request,
    status: update.status as FileRequestStatus,
    failureReason: update.failureReason,
    selectedTransportMode: update.selectedTransportMode !== undefined
      ? update.selectedTransportMode as TransferTransportMode | null
      : request.selectedTransportMode,
    failureCategory: update.failureCategory !== undefined
      ? update.failureCategory as TransferFailureCategory | null
      : request.failureCategory,
    bytesTransferred: update.bytesTransferred ?? request.bytesTransferred,
    totalBytes: update.totalBytes !== undefined ? update.totalBytes : request.totalBytes,
  }
}

function AppInner() {
  const [configured, setConfigured] = useState(() => loadConfig() !== null)
  const queryClient = useQueryClient()

  const { state: connectionState } = useSignalR({
    onFileRequestUpdate: (update) => {
      const requests = queryClient.getQueryData<FileRequest[]>(requestsQueryKey)
      if (requests?.some(request => request.id === update.fileRequestId)) {
        queryClient.setQueryData<FileRequest[]>(requestsQueryKey, requests.map(request => request.id === update.fileRequestId
          ? applyFileRequestUpdate(request, update)
          : request))
      } else {
        void queryClient.invalidateQueries({ queryKey: requestsQueryKey })
      }

      if (update.status === 'Completed' || update.status === 'Failed' || update.status === 'Cancelled') {
        queryClient.setQueryData<Record<string, TransferProgress>>(transferProgressQueryKey, current => {
          if (!current) return current
          const next = { ...current }
          delete next[update.fileRequestId]
          return next
        })
      }

      queryClient.setQueryData(requestsLiveUpdatedAtQueryKey, Date.now())
    },
    onTransferProgress: (progress) => {
      queryClient.setQueryData<Record<string, TransferProgress>>(transferProgressQueryKey, current => ({
        ...current,
        [progress.fileRequestId]: progress,
      }))
      queryClient.setQueryData(requestsLiveUpdatedAtQueryKey, Date.now())
    },
  })

  useEffect(() => {
    function handleAuthInvalid() {
      setConfigured(false)
    }
    window.addEventListener('jf-auth-invalid', handleAuthInvalid)
    return () => window.removeEventListener('jf-auth-invalid', handleAuthInvalid)
  }, [])

  if (!configured) {
    return <Setup onComplete={() => setConfigured(true)} />
  }

  return (
    <BrowserRouter>
      <Layout connectionState={connectionState}>
        <Routes>
          <Route path="/" element={<Dashboard connectionState={connectionState} />} />
          <Route path="/my-media" element={<MyMedia />} />
          <Route path="/library" element={<Library />} />
          <Route path="/invitations" element={<Invitations />} />
          <Route path="/requests" element={<Requests />} />
          <Route path="/settings" element={<Settings onLogout={() => setConfigured(false)} />} />
          <Route path="*" element={<Navigate to="/" />} />
        </Routes>
      </Layout>
    </BrowserRouter>
  )
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <AppInner />
    </QueryClientProvider>
  )
}
