import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { AlertCircle, CheckCircle2, Clock, Download, Loader2, Radio, X } from 'lucide-react'
import { fileRequestsApi } from '../api/client'
import type { FileRequest, FileRequestStatus } from '../api/types'
import { Badge } from '../components/Badge'
import { Card } from '../components/Card'
import { useConfig } from '../hooks/useConfig'
import { formatBytes } from '../utils/formatBytes'
import { formatDateTime } from '../utils/formatDate'
import type { FileRequestUpdate, TransferProgress } from '../hooks/useSignalR'

interface RequestsProps {
  latestUpdate: FileRequestUpdate | null
  latestProgress: TransferProgress | null
}

const statusIcon: Record<FileRequestStatus, React.ReactNode> = {
  Pending: <Clock size={14} className="text-yellow-400" />,
  HolePunching: <Radio size={14} className="text-blue-400 animate-pulse" />,
  Transferring: <Loader2 size={14} className="text-[var(--color-accent)] animate-spin" />,
  Completed: <CheckCircle2 size={14} className="text-emerald-400" />,
  Failed: <AlertCircle size={14} className="text-red-400" />,
  Cancelled: <X size={14} className="text-zinc-400" />,
}

const statusVariant: Record<FileRequestStatus, 'default' | 'success' | 'warning' | 'danger' | 'neutral'> = {
  Pending: 'warning',
  HolePunching: 'default',
  Transferring: 'default',
  Completed: 'success',
  Failed: 'danger',
  Cancelled: 'neutral',
}

const cancellableStatuses: FileRequestStatus[] = ['Pending', 'HolePunching', 'Transferring']

function RequestRow({
  req,
  myServerId,
  progress,
  onCancel,
  cancelling,
}: {
  req: FileRequest
  myServerId: string
  progress: TransferProgress | null
  onCancel: (id: string) => void
  cancelling: boolean
}) {
  const isIncoming = req.owningServerId.toLowerCase() === myServerId.toLowerCase()
  const peerName = isIncoming ? req.requestingServerName : req.owningServerName
  const pct = progress && progress.totalBytes > 0
    ? Math.min(100, Math.round((progress.bytesReceived / progress.totalBytes) * 100))
    : null
  const persistedPct = req.totalBytes && req.totalBytes > 0
    ? Math.min(100, Math.round((req.bytesTransferred / req.totalBytes) * 100))
    : null
  const canCancel = cancellableStatuses.includes(req.status)

  return (
    <Card className="flex items-start gap-4">
      <div className="w-9 h-9 rounded-lg bg-white/5 flex items-center justify-center shrink-0 mt-0.5">
        {statusIcon[req.status]}
      </div>
      <div className="flex-1 min-w-0">
        <div className="flex items-center gap-2 flex-wrap">
          <p className="text-sm font-medium text-[var(--color-heading)] truncate">
            {req.itemTitle ?? req.jellyfinItemId}
          </p>
          <Badge variant="neutral">{isIncoming ? 'Incoming' : 'Outgoing'}</Badge>
        </div>
        <p className="text-xs text-[var(--color-text)] mt-0.5">
          {isIncoming ? 'from' : 'to'}{' '}
          <span className="text-[var(--color-heading)]">{peerName}</span>
          {' · '}{formatDateTime(req.createdAt)}
        </p>
        <p className="text-xs text-[var(--color-text)] mt-1">
          Mode: <span className="text-[var(--color-heading)]">{req.selectedTransportMode ?? 'n/a'}</span>
          {req.failureCategory ? (
            <>
              {' · '}Failure Category:{' '}
              <span className="text-[var(--color-heading)]">{req.failureCategory}</span>
            </>
          ) : null}
        </p>
        {req.status === 'Transferring' && pct !== null && (
          <div className="mt-2">
            <div className="flex justify-between text-xs text-[var(--color-text)] mb-1">
              <span>{pct}%</span>
              <span>{formatBytes(progress!.bytesReceived)} / {formatBytes(progress!.totalBytes)}</span>
            </div>
            <div className="h-1.5 rounded-full bg-white/10 overflow-hidden">
              <div
                className="h-full rounded-full bg-[var(--color-accent)] transition-all duration-500"
                style={{ width: `${pct}%` }}
              />
            </div>
          </div>
        )}
        {req.status !== 'Transferring' && persistedPct !== null && req.totalBytes !== null && (
          <p className="text-xs text-[var(--color-text)] mt-2">
            Progress snapshot: {persistedPct}% ({formatBytes(req.bytesTransferred)} / {formatBytes(req.totalBytes)})
          </p>
        )}
        {req.failureReason && (
          <p className="text-xs text-red-400 mt-2 leading-relaxed">{req.failureReason}</p>
        )}
      </div>
      <div className="flex items-center gap-2 shrink-0">
        <Badge variant={statusVariant[req.status]}>{req.status}</Badge>
        {canCancel && (
          <button
            onClick={() => onCancel(req.id)}
            disabled={cancelling}
            className="w-7 h-7 rounded-lg flex items-center justify-center text-zinc-400 hover:text-red-400 hover:bg-red-400/10 transition-colors disabled:opacity-40"
            title="Cancel"
          >
            {cancelling ? <Loader2 size={13} className="animate-spin" /> : <X size={13} />}
          </button>
        )}
      </div>
    </Card>
  )
}

export function Requests({ latestUpdate, latestProgress }: RequestsProps) {
  const cfg = useConfig()
  const qc = useQueryClient()
  const [progressMap, setProgressMap] = useState<Record<string, TransferProgress>>({})
  const [cancellingIds, setCancellingIds] = useState<Set<string>>(new Set())

  const { data: requests, isLoading } = useQuery({
    queryKey: ['requests'],
    queryFn: fileRequestsApi.list,
  })

  const cancelMutation = useMutation({
    mutationFn: (id: string) => fileRequestsApi.cancel(id),
    onMutate: (id) => setCancellingIds(prev => new Set([...prev, id])),
    onSettled: (_, __, id) => {
      setCancellingIds(prev => { const s = new Set(prev); s.delete(id); return s })
      qc.invalidateQueries({ queryKey: ['requests'] })
    },
  })

  // Refresh list whenever a SignalR status update arrives
  useEffect(() => {
    if (latestUpdate) qc.invalidateQueries({ queryKey: ['requests'] })
  }, [latestUpdate, qc])

  // Accumulate progress updates in local state (no refetch needed)
  useEffect(() => {
    if (latestProgress) {
      setProgressMap(prev => ({ ...prev, [latestProgress.fileRequestId]: latestProgress }))
    }
  }, [latestProgress])

  const active = requests?.filter(r =>
    r.status === 'Pending' || r.status === 'HolePunching' || r.status === 'Transferring'
  ) ?? []
  const done = requests?.filter(r =>
    r.status === 'Completed' || r.status === 'Failed' || r.status === 'Cancelled'
  ) ?? []

  return (
    <div className="max-w-3xl">
      <div className="flex items-center justify-between mb-8">
        <div>
          <h1 className="text-2xl font-semibold text-[var(--color-heading)]">Requests</h1>
          <p className="text-sm text-[var(--color-text)] mt-1">File transfer queue and history</p>
        </div>
        <div className="flex items-center gap-2 text-xs text-[var(--color-text)]">
          <Download size={13} />
          {latestUpdate
            ? <span className="text-[var(--color-accent)]">Live updates active</span>
            : 'Waiting for updates…'}
        </div>
      </div>

      {isLoading && (
        <div className="flex items-center justify-center py-20 text-[var(--color-text)]">
          <Loader2 size={20} className="animate-spin mr-2" />
          Loading…
        </div>
      )}

      {!isLoading && requests?.length === 0 && (
        <Card className="text-center py-16">
          <div className="w-12 h-12 rounded-xl bg-[var(--color-accent-dim)] flex items-center justify-center mx-auto mb-3">
            <Download size={20} className="text-[var(--color-accent)]" />
          </div>
          <p className="text-[var(--color-heading)] font-medium">No file requests yet</p>
          <p className="text-sm text-[var(--color-text)] mt-1">
            Browse the library and click Request on a media item
          </p>
        </Card>
      )}

      {active.length > 0 && (
        <section className="mb-8">
          <h2 className="text-sm font-semibold text-[var(--color-heading)] mb-3 flex items-center gap-2">
            <Radio size={14} className="text-[var(--color-accent)]" />
            Active
            <span className="ml-auto text-xs text-[var(--color-text)] font-normal">{active.length}</span>
          </h2>
          <div className="flex flex-col gap-2">
            {active.map(r => (
              <RequestRow
                key={r.id}
                req={r}
                myServerId={cfg?.serverId ?? ''}
                progress={progressMap[r.id] ?? null}
                onCancel={(id) => cancelMutation.mutate(id)}
                cancelling={cancellingIds.has(r.id)}
              />
            ))}
          </div>
        </section>
      )}

      {done.length > 0 && (
        <section>
          <h2 className="text-sm font-semibold text-[var(--color-heading)] mb-3 flex items-center gap-2">
            <CheckCircle2 size={14} />
            History
            <span className="ml-auto text-xs text-[var(--color-text)] font-normal">{done.length}</span>
          </h2>
          <div className="flex flex-col gap-2">
            {done.map(r => (
              <RequestRow
                key={r.id}
                req={r}
                myServerId={cfg?.serverId ?? ''}
                progress={null}
                onCancel={() => {}}
                cancelling={false}
              />
            ))}
          </div>
        </section>
      )}
    </div>
  )
}
