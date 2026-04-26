import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { AlertCircle, CheckCircle2, Clock, Download, Loader2, Radio, X } from 'lucide-react'
import { fileRequestsApi, getErrorDescription } from '../api/client'
import { requestsLiveUpdatedAtQueryKey, requestsQueryKey, transferProgressQueryKey } from '../api/queryKeys'
import type { FileRequest, FileRequestStatus } from '../api/types'
import { Card } from '../components/Card'
import { ConfirmDialog } from '../components/ConfirmDialog'
import { EmptyState } from '../components/EmptyState'
import { IconButton } from '../components/IconButton'
import { PageHeader } from '../components/PageHeader'
import { SectionHeader } from '../components/SectionHeader'
import { StatusPill } from '../components/StatusPill'
import { useToast } from '../hooks/useToast'
import { useConfig } from '../hooks/useConfig'
import { formatBytes } from '../utils/formatBytes'
import { formatDateTime } from '../utils/formatDate'
import type { TransferProgress } from '../hooks/useSignalR'

const statusIcon: Record<FileRequestStatus, React.ReactNode> = {
  Pending: <Clock size={14} className="text-yellow-400" />,
  HolePunching: <Radio size={14} className="text-blue-400 animate-pulse" />,
  Transferring: <Loader2 size={14} className="text-[var(--color-accent)] animate-spin" />,
  Completed: <CheckCircle2 size={14} className="text-emerald-400" />,
  Failed: <AlertCircle size={14} className="text-red-400" />,
  Cancelled: <X size={14} className="text-zinc-400" />,
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
  onCancel: (request: FileRequest) => void
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
  const activeTransfer = req.status === 'Transferring' && pct !== null

  return (
    <Card className={`overflow-hidden ${req.status === 'Failed' ? 'border-red-900/40 bg-red-950/10' : req.status === 'Completed' ? 'border-emerald-900/30 bg-emerald-950/10' : canCancel ? 'border-[var(--color-accent)]/25 bg-[var(--color-accent-dim)]/20' : ''}`}>
      <div className="flex items-start gap-4">
        <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-white/5 mt-0.5">
          {statusIcon[req.status]}
        </div>
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2 flex-wrap">
            <p className="truncate text-sm font-medium text-[var(--color-heading)]">{req.itemTitle ?? req.jellyfinItemId}</p>
            <span className="rounded-full bg-white/5 px-2 py-0.5 text-xs font-medium text-[var(--color-text)]">{isIncoming ? 'Incoming' : 'Outgoing'}</span>
          </div>
          <p className="mt-0.5 text-xs text-[var(--color-text)]">
            {isIncoming ? 'from' : 'to'} <span className="text-[var(--color-heading)]">{peerName}</span> · {formatDateTime(req.createdAt)}
          </p>
          <p className="mt-1 text-xs text-[var(--color-text)]">
            Mode: <span className="text-[var(--color-heading)]">{req.selectedTransportMode ?? 'n/a'}</span>
            {req.failureCategory ? <> · Failure Category: <span className="text-[var(--color-heading)]">{req.failureCategory}</span></> : null}
          </p>
        </div>
        <div className="flex shrink-0 items-center gap-2">
          <StatusPill status={req.status} />
          {canCancel && (
            <IconButton
              label={`Cancel request for ${req.itemTitle ?? req.jellyfinItemId}`}
              icon={cancelling ? <Loader2 size={13} className="animate-spin" /> : <X size={13} />}
              variant="danger"
              onClick={() => onCancel(req)}
              disabled={cancelling}
            />
          )}
        </div>
      </div>

      {activeTransfer && (
        <div className="mt-4 rounded-xl border border-[var(--color-accent)]/20 bg-black/15 p-3">
          <div className="mb-2 flex items-center justify-between text-xs">
            <span className="font-medium text-[var(--color-heading)]">Transfer progress</span>
            <span className="text-[var(--color-accent)]">{pct}%</span>
          </div>
          <div className="mb-2 h-2.5 overflow-hidden rounded-full bg-white/10">
            <div className="h-full rounded-full bg-gradient-to-r from-[var(--color-accent)] to-emerald-400 transition-all duration-500" style={{ width: `${pct}%` }} />
          </div>
          <div className="flex justify-between text-xs text-[var(--color-text)]">
            <span>{formatBytes(progress!.bytesReceived)} received</span>
            <span>{formatBytes(progress!.totalBytes)} total</span>
          </div>
        </div>
      )}

      {!activeTransfer && persistedPct !== null && req.totalBytes !== null && (
        <p className="mt-3 text-xs text-[var(--color-text)]">
          Progress snapshot: {persistedPct}% ({formatBytes(req.bytesTransferred)} / {formatBytes(req.totalBytes)})
        </p>
      )}
      {req.failureReason && <p className="mt-3 text-xs leading-relaxed text-red-400">{req.failureReason}</p>}
    </Card>
  )
}

export function Requests() {
  const cfg = useConfig()
  const qc = useQueryClient()
  const toast = useToast()
  const [cancellingIds, setCancellingIds] = useState<Set<string>>(new Set())
  const [requestToCancel, setRequestToCancel] = useState<FileRequest | null>(null)

  const { data: requests, isLoading } = useQuery({ queryKey: requestsQueryKey, queryFn: fileRequestsApi.list })
  const { data: progressMap = {} } = useQuery({
    queryKey: transferProgressQueryKey,
    queryFn: () => Promise.resolve({} as Record<string, TransferProgress>),
    initialData: {} as Record<string, TransferProgress>,
    staleTime: Infinity,
  })
  const { data: liveUpdatedAt = null } = useQuery({
    queryKey: requestsLiveUpdatedAtQueryKey,
    queryFn: () => Promise.resolve(null as number | null),
    initialData: null as number | null,
    staleTime: Infinity,
  })

  const cancelMutation = useMutation({
    mutationFn: (id: string) => fileRequestsApi.cancel(id),
    onMutate: (id) => setCancellingIds(prev => new Set([...prev, id])),
    onSuccess: (_data, id) => {
      const title = requests?.find(r => r.id === id)?.itemTitle
      setRequestToCancel(null)
      toast.info('Request cancelled', title ?? undefined)
    },
    onError: (err) => toast.error('Could not cancel request', getErrorDescription(err, 'Failed to cancel request')),
    onSettled: (_, __, id) => {
      setCancellingIds(prev => { const s = new Set(prev); s.delete(id); return s })
      qc.invalidateQueries({ queryKey: requestsQueryKey })
    },
  })

  const active = requests?.filter(r => r.status === 'Pending' || r.status === 'HolePunching' || r.status === 'Transferring') ?? []
  const done = requests?.filter(r => r.status === 'Completed' || r.status === 'Failed' || r.status === 'Cancelled') ?? []

  return (
    <div className="max-w-3xl">
      <PageHeader
        eyebrow="Transfers"
        title="Requests"
        description="File transfer queue and history for incoming and outgoing media requests."
        icon={<Download size={18} />}
        action={<div className="flex items-center gap-2 rounded-full border border-[var(--color-border)] bg-white/5 px-3 py-1.5 text-xs text-[var(--color-text)]"><Download size={13} />{liveUpdatedAt ? <span className="text-[var(--color-accent)]">Live updates active</span> : 'Waiting for updates…'}</div>}
      />

      {isLoading && <div className="flex items-center justify-center py-20 text-[var(--color-text)]"><Loader2 size={20} className="animate-spin mr-2" />Loading…</div>}
      {!isLoading && requests?.length === 0 && <EmptyState icon={<Download size={20} />} title="No file requests yet" description="Browse the library and click Request on a media item. Active transfers and history will appear here." />}

      {active.length > 0 && (
        <section className="mb-8">
          <SectionHeader title="Active" icon={<Radio size={14} />} count={active.length} />
          <div className="flex flex-col gap-2">
            {active.map(r => <RequestRow key={r.id} req={r} myServerId={cfg?.serverId ?? ''} progress={progressMap[r.id] ?? null} onCancel={setRequestToCancel} cancelling={cancellingIds.has(r.id)} />)}
          </div>
        </section>
      )}

      {done.length > 0 && (
        <section>
          <SectionHeader title="History" icon={<CheckCircle2 size={14} />} count={done.length} />
          <div className="flex flex-col gap-2">
            {done.map(r => <RequestRow key={r.id} req={r} myServerId={cfg?.serverId ?? ''} progress={null} onCancel={() => {}} cancelling={false} />)}
          </div>
        </section>
      )}

      <ConfirmDialog
        open={requestToCancel !== null}
        title="Cancel transfer request?"
        description={requestToCancel ? <>This will cancel <span className="text-[var(--color-heading)]">{requestToCancel.itemTitle ?? requestToCancel.jellyfinItemId}</span>. You can request it again later.</> : ''}
        confirmLabel="Cancel request"
        variant="danger"
        onConfirm={() => { if (requestToCancel) cancelMutation.mutate(requestToCancel.id) }}
        onCancel={() => setRequestToCancel(null)}
      />
    </div>
  )
}
