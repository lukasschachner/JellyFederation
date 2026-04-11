import { useState } from 'react'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { loadConfig } from './lib/config'
import { useSignalR } from './hooks/useSignalR'
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

function AppInner() {
  const [configured, setConfigured] = useState(() => loadConfig() !== null)
  const [latestUpdate, setLatestUpdate] = useState<FileRequestUpdate | null>(null)
  const [latestProgress, setLatestProgress] = useState<TransferProgress | null>(null)

  const { state: connectionState } = useSignalR({
    onFileRequestUpdate: (update) => setLatestUpdate(update),
    onTransferProgress: (progress) => setLatestProgress(progress),
  })

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
          <Route path="/requests" element={<Requests latestUpdate={latestUpdate} latestProgress={latestProgress} />} />
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
