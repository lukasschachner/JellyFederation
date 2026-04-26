import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { BookOpen, Check, Download, Loader2, Search } from 'lucide-react'
import { fileRequestsApi, getErrorDescription, libraryApi } from '../api/client'
import type { MediaItem, MediaType } from '../api/types'
import { useToast } from '../hooks/useToast'
import { Badge } from '../components/Badge'
import { Button } from '../components/Button'
import { Card } from '../components/Card'
import { EmptyState } from '../components/EmptyState'
import { MediaGridSkeleton } from '../components/MediaSkeletons'
import { MediaSearchBox } from '../components/MediaSearchBox'
import { MediaTypeIcon } from '../components/MediaTypeIcon'
import { MediaTypeTabs } from '../components/MediaTypeTabs'
import { PageHeader } from '../components/PageHeader'
import { Paginator } from '../components/Paginator'
import { formatBytes } from '../utils/formatBytes'
import { useMediaFilter } from '../hooks/useMediaFilter'

const PAGE_SIZE = 100

const typeBadge: Record<MediaType, 'default' | 'success' | 'warning' | 'neutral'> = {
  Movie: 'default',
  Series: 'success',
  Episode: 'neutral',
  Music: 'warning',
  Other: 'neutral',
}

type RequestState = 'idle' | 'pending' | 'success'

function MediaCard({
  item,
  onRequest,
  state,
}: {
  item: MediaItem
  onRequest: (item: MediaItem) => void
  state: RequestState
}) {
  const [imageFailed, setImageFailed] = useState(false)
  const showImage = Boolean(item.imageUrl) && !imageFailed

  return (
    <Card variant="interactive" className="group overflow-hidden p-0">
      <div className="relative flex aspect-video items-center justify-center overflow-hidden bg-gradient-to-br from-white/[0.06] to-black/20 text-[var(--color-text)]">
        {showImage ? (
          <img
            src={item.imageUrl ?? undefined}
            alt={item.title}
            className="h-full w-full object-cover transition-transform duration-300 group-hover:scale-105"
            loading="lazy"
            onError={() => setImageFailed(true)}
          />
        ) : (
          <div className="flex h-14 w-14 items-center justify-center rounded-2xl border border-white/10 bg-white/5">
            <MediaTypeIcon type={item.type} size={26} />
          </div>
        )}
        <div className="absolute inset-x-0 bottom-0 bg-gradient-to-t from-black/75 to-transparent p-3">
          <p className="truncate text-sm font-semibold text-white drop-shadow">{item.title}</p>
          <div className="mt-1 flex items-center gap-2">
            <Badge variant={typeBadge[item.type]}>{item.type}</Badge>
            {item.year && <span className="text-xs text-zinc-300">{item.year}</span>}
          </div>
        </div>
      </div>

      <div className="flex min-h-[116px] flex-col p-4">
        {item.overview ? (
          <p className="line-clamp-2 text-xs leading-5 text-[var(--color-text)]">{item.overview}</p>
        ) : (
          <p className="text-xs leading-5 text-[var(--color-text-muted)]">No overview available.</p>
        )}

        <div className="mt-auto flex items-center justify-between gap-3 border-t border-[var(--color-border)] pt-3">
          <div className="min-w-0 text-xs text-[var(--color-text)]">
            <p className="truncate text-[var(--color-heading)]">{item.serverName}</p>
            <p>{formatBytes(item.fileSizeBytes)}</p>
          </div>
          <Button
            size="sm"
            variant={state === 'success' ? 'secondary' : 'primary'}
            disabled={state !== 'idle'}
            onClick={() => onRequest(item)}
            aria-label={`Request ${item.title}`}
            className="shrink-0"
          >
            {state === 'pending' && <Loader2 size={12} className="animate-spin" />}
            {state === 'success' && <Check size={12} className="text-emerald-400" />}
            {state === 'idle' && <Download size={12} />}
            {state === 'pending' ? 'Queuing…' : state === 'success' ? 'Queued' : 'Request'}
          </Button>
        </div>
      </div>
    </Card>
  )
}

export function Library() {
  const { search, debouncedSearch, activeTab, page, setPage, handleSearch, handleTab } = useMediaFilter()
  const [pendingIds, setPendingIds] = useState<Set<string>>(new Set())
  const [recentlyQueuedIds, setRecentlyQueuedIds] = useState<Set<string>>(new Set())
  const queryClient = useQueryClient()
  const toast = useToast()

  const { data: counts } = useQuery({
    queryKey: ['library-counts', debouncedSearch],
    queryFn: () => libraryApi.browseCounts(debouncedSearch || undefined),
  })

  const { data: items, isLoading, error } = useQuery({
    queryKey: ['library', debouncedSearch, activeTab, page],
    queryFn: () => libraryApi.browse(debouncedSearch || undefined, activeTab, page, PAGE_SIZE),
  })

  const totalForTab = counts?.[activeTab] ?? 0
  const totalPages = Math.ceil(totalForTab / PAGE_SIZE)

  const requestMutation = useMutation({
    mutationFn: (item: MediaItem) => fileRequestsApi.create(item.jellyfinItemId, item.serverId),
    onMutate: (item) => {
      setPendingIds(prev => new Set(prev).add(item.id))
    },
    onSuccess: (_data, item) => {
      queryClient.invalidateQueries({ queryKey: ['requests'] })
      toast.success('Added to receive queue', item.title)
      setRecentlyQueuedIds(prev => new Set(prev).add(item.id))
      window.setTimeout(() => {
        setRecentlyQueuedIds(prev => {
          const next = new Set(prev)
          next.delete(item.id)
          return next
        })
      }, 2500)
    },
    onError: (err, item) => {
      toast.error(
        `Couldn't queue ${item.title}`,
        getErrorDescription(err, 'Request failed'),
      )
    },
    onSettled: (_data, _err, item) => {
      setPendingIds(prev => {
        const next = new Set(prev)
        next.delete(item.id)
        return next
      })
    },
  })

  return (
    <div className="max-w-6xl">
      <PageHeader
        eyebrow="Browse"
        title="Library"
        description="Media from servers that have accepted your invitation. Queue items here to receive them."
        icon={<BookOpen size={18} />}
        action={counts && <span className="rounded-full border border-[var(--color-border)] bg-white/5 px-3 py-1.5 text-sm text-[var(--color-text)]">{counts['All'] ?? 0} items total</span>}
      />

      <MediaTypeTabs activeTab={activeTab} counts={counts} onTabChange={handleTab} />

      <MediaSearchBox
        label="Search library titles"
        value={search}
        onChange={handleSearch}
      />

      {isLoading && <MediaGridSkeleton />}

      {error && (
        <Card className="text-center py-10">
          <p className="text-[var(--color-danger)]">Failed to load library. Is the federation server reachable?</p>
        </Card>
      )}

      {!isLoading && !error && items?.length === 0 && (counts === undefined || (counts?.['All'] ?? 0) === 0) && (
        <EmptyState
          icon={<Search size={20} />}
          title="No items found"
          description={debouncedSearch
            ? 'Try a different search term or switch media type filters.'
            : 'Accept some invitations to browse federated libraries.'}
        />
      )}

      {!isLoading && items && items.length > 0 && (
        <>
          <div className="mb-6 grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
            {items.map(item => {
              const state: RequestState = pendingIds.has(item.id)
                ? 'pending'
                : recentlyQueuedIds.has(item.id)
                  ? 'success'
                  : 'idle'
              return (
                <MediaCard
                  key={item.id}
                  item={item}
                  state={state}
                  onRequest={item => requestMutation.mutate(item)}
                />
              )
            })}
          </div>

          <Paginator page={page} totalPages={totalPages} totalItems={totalForTab} onPageChange={setPage} />
        </>
      )}
    </div>
  )
}
