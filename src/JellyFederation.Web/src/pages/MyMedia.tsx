import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { libraryApi } from '../api/client'
import type { MediaItem } from '../api/types'
import { Badge } from '../components/Badge'
import { Card } from '../components/Card'
import { MediaListSkeleton } from '../components/MediaSkeletons'
import { MediaSearchBox } from '../components/MediaSearchBox'
import { MediaTypeIcon } from '../components/MediaTypeIcon'
import { MediaTypeTabs } from '../components/MediaTypeTabs'
import { Paginator } from '../components/Paginator'
import { formatBytes } from '../utils/formatBytes'
import { useMediaFilter } from '../hooks/useMediaFilter'

const PAGE_SIZE = 100

function RequestableToggle({ item }: { item: MediaItem }) {
  const queryClient = useQueryClient()
  const mutation = useMutation({
    mutationFn: (value: boolean) => libraryApi.setRequestable(item.id, value),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['my-media'] })
      queryClient.invalidateQueries({ queryKey: ['my-media-counts'] })
    },
  })

  return (
    <button
      type="button"
      role="switch"
      aria-checked={item.isRequestable}
      aria-label={`${item.title} is ${item.isRequestable ? 'requestable' : 'private'}`}
      onClick={() => mutation.mutate(!item.isRequestable)}
      disabled={mutation.isPending}
      title={item.isRequestable ? 'Click to make private' : 'Click to make requestable'}
      className={`relative inline-flex h-5 w-9 shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors focus:outline-none disabled:opacity-50 ${
        item.isRequestable ? 'bg-[var(--color-accent)]' : 'bg-white/20'
      }`}
    >
      <span
        className={`pointer-events-none inline-block h-4 w-4 transform rounded-full bg-white shadow transition-transform ${
          item.isRequestable ? 'translate-x-4' : 'translate-x-0'
        }`}
      />
    </button>
  )
}

export function MyMedia() {
  const { search, debouncedSearch, activeTab, page, setPage, handleSearch, handleTab } = useMediaFilter()

  const { data: counts } = useQuery({
    queryKey: ['my-media-counts', debouncedSearch],
    queryFn: () => libraryApi.mineCounts(debouncedSearch || undefined),
  })

  const { data: items, isLoading, error } = useQuery({
    queryKey: ['my-media', debouncedSearch, activeTab, page],
    queryFn: () => libraryApi.mine(debouncedSearch || undefined, activeTab, page, PAGE_SIZE),
  })

  const totalForTab = counts?.[activeTab] ?? 0
  const totalPages = Math.ceil(totalForTab / PAGE_SIZE)

  return (
    <div className="max-w-6xl">
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-2xl font-semibold text-[var(--color-heading)]">My Media</h1>
          <p className="text-sm text-[var(--color-text)] mt-1">
            Manage which items federated peers can request
          </p>
        </div>
        {counts && (
          <span className="text-sm text-[var(--color-text)]">
            {counts['All'] ?? 0} items total
          </span>
        )}
      </div>

      <MediaTypeTabs activeTab={activeTab} counts={counts} onTabChange={handleTab} />

      <MediaSearchBox
        label="Search my media titles"
        value={search}
        onChange={handleSearch}
        className="mb-4"
      />

      {isLoading && <MediaListSkeleton />}

      {error && (
        <Card className="text-center py-10">
          <p className="text-[var(--color-danger)]">Failed to load library. Is the federation server reachable?</p>
        </Card>
      )}

      {!isLoading && !error && (counts?.['All'] ?? 0) === 0 && (
        <Card className="text-center py-16">
          <p className="text-[var(--color-heading)] font-medium">No items synced yet</p>
          <p className="text-sm text-[var(--color-text)] mt-1">
            Make sure the Jellyfin plugin is configured and running
          </p>
        </Card>
      )}

      {!isLoading && items && items.length > 0 && (
        <>
          <div className="space-y-1 mb-4">
            {items.map(item => (
              <div
                key={item.id}
                className="flex items-center gap-4 px-4 py-3 rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] hover:border-[var(--color-accent)]/30 transition-colors"
              >
                <div className="text-[var(--color-text)] shrink-0">
                  <MediaTypeIcon type={item.type} />
                </div>

                <div className="flex-1 min-w-0 flex items-center gap-3">
                  <p className="text-sm font-medium text-[var(--color-heading)] truncate">
                    {item.title}
                  </p>
                  {item.year && (
                    <span className="text-xs text-[var(--color-text)] shrink-0">{item.year}</span>
                  )}
                  {activeTab === 'All' && (
                    <Badge variant="neutral">{item.type}</Badge>
                  )}
                </div>

                <span className="text-xs text-[var(--color-text)] shrink-0 hidden sm:block">
                  {formatBytes(item.fileSizeBytes)}
                </span>

                <div className="flex items-center gap-2 shrink-0">
                  <span className="text-xs text-[var(--color-text)] hidden md:block">
                    {item.isRequestable ? 'Requestable' : 'Private'}
                  </span>
                  <RequestableToggle item={item} />
                </div>
              </div>
            ))}
          </div>

          <Paginator page={page} totalPages={totalPages} totalItems={totalForTab} onPageChange={setPage} />
        </>
      )}
    </div>
  )
}
