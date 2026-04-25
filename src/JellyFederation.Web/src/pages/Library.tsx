import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { Download, Search } from 'lucide-react'
import { libraryApi, fileRequestsApi } from '../api/client'
import type { MediaItem, MediaType } from '../api/types'
import { Badge } from '../components/Badge'
import { Button } from '../components/Button'
import { Card } from '../components/Card'
import { MediaGridSkeleton } from '../components/MediaSkeletons'
import { MediaSearchBox } from '../components/MediaSearchBox'
import { MediaTypeIcon } from '../components/MediaTypeIcon'
import { MediaTypeTabs } from '../components/MediaTypeTabs'
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

function MediaCard({ item, onRequest }: { item: MediaItem; onRequest: (item: MediaItem) => void }) {
  const [imageFailed, setImageFailed] = useState(false)
  const showImage = Boolean(item.imageUrl) && !imageFailed

  return (
    <Card className="flex flex-col gap-3 hover:border-[var(--color-accent)]/40 transition-colors">
      <div className="w-full aspect-video bg-white/5 rounded-lg overflow-hidden flex items-center justify-center text-[var(--color-text)]">
        {showImage ? (
          <img
            src={item.imageUrl ?? undefined}
            alt={item.title}
            className="w-full h-full object-cover"
            loading="lazy"
            onError={() => setImageFailed(true)}
          />
        ) : (
          <MediaTypeIcon type={item.type} size={24} />
        )}
      </div>

      <div className="flex-1 min-w-0">
        <p className="text-sm font-medium text-[var(--color-heading)] truncate">{item.title}</p>
        <div className="flex items-center gap-2 mt-1">
          <Badge variant={typeBadge[item.type]}>{item.type}</Badge>
          {item.year && <span className="text-xs text-[var(--color-text)]">{item.year}</span>}
        </div>
        {item.overview && (
          <p className="text-xs text-[var(--color-text)] mt-2 line-clamp-2">{item.overview}</p>
        )}
      </div>

      <div className="flex items-center justify-between pt-2 border-t border-[var(--color-border)]">
        <div className="text-xs text-[var(--color-text)]">
          <span className="text-[var(--color-heading)]">{item.serverName}</span>
          <span className="mx-1">·</span>
          {formatBytes(item.fileSizeBytes)}
        </div>
        <Button size="sm" variant="primary" onClick={() => onRequest(item)}>
          <Download size={12} />
          Request
        </Button>
      </div>
    </Card>
  )
}

export function Library() {
  const { search, debouncedSearch, activeTab, page, setPage, handleSearch, handleTab } = useMediaFilter()
  const [requestError, setRequestError] = useState<string | null>(null)
  const queryClient = useQueryClient()

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
    onSuccess: () => {
      setRequestError(null)
      queryClient.invalidateQueries({ queryKey: ['requests'] })
    },
    onError: (err) => {
      setRequestError(err instanceof Error ? err.message : 'Request failed')
    },
  })

  return (
    <div className="max-w-6xl">
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-2xl font-semibold text-[var(--color-heading)]">Library</h1>
          <p className="text-sm text-[var(--color-text)] mt-1">
            Media from servers that have accepted your invitation
          </p>
        </div>
        {counts && (
          <span className="text-sm text-[var(--color-text)]">{counts['All'] ?? 0} items total</span>
        )}
      </div>

      <MediaTypeTabs activeTab={activeTab} counts={counts} onTabChange={handleTab} />

      <MediaSearchBox
        label="Search library titles"
        value={search}
        onChange={handleSearch}
      />

      {isLoading && <MediaGridSkeleton />}

      {requestError && (
        <div className="mb-4 px-4 py-3 rounded-lg bg-red-950/40 border border-red-900/40 text-sm text-red-400 flex items-center justify-between">
          <span>{requestError}</span>
          <button type="button" aria-label="Dismiss request error" onClick={() => setRequestError(null)} className="text-red-400 hover:text-red-300 ml-4 cursor-pointer">×</button>
        </div>
      )}

      {error && (
        <Card className="text-center py-10">
          <p className="text-[var(--color-danger)]">Failed to load library. Is the federation server reachable?</p>
        </Card>
      )}

      {!isLoading && !error && items?.length === 0 && (counts === undefined || (counts?.['All'] ?? 0) === 0) && (
        <Card className="text-center py-16">
          <div className="w-12 h-12 rounded-xl bg-[var(--color-accent-dim)] flex items-center justify-center mx-auto mb-3">
            <Search size={20} className="text-[var(--color-accent)]" />
          </div>
          <p className="text-[var(--color-heading)] font-medium">No items found</p>
          <p className="text-sm text-[var(--color-text)] mt-1">
            {debouncedSearch
              ? 'Try a different search term'
              : 'Accept some invitations to browse federated libraries'}
          </p>
        </Card>
      )}

      {!isLoading && items && items.length > 0 && (
        <>
          <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 gap-4 mb-6">
            {items.map(item => (
              <MediaCard
                key={item.id}
                item={item}
                onRequest={item => requestMutation.mutate(item)}
              />
            ))}
          </div>

          <Paginator page={page} totalPages={totalPages} totalItems={totalForTab} onPageChange={setPage} />
        </>
      )}
    </div>
  )
}
