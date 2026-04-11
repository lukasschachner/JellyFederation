import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { ChevronLeft, ChevronRight, Film, Mic2, MonitorPlay, Search, Tv } from 'lucide-react'
import { libraryApi } from '../api/client'
import type { MediaItem, MediaType } from '../api/types'
import { Badge } from '../components/Badge'
import { Card } from '../components/Card'

const PAGE_SIZE = 100

const typeIcon: Record<MediaType, React.ReactNode> = {
  Movie: <Film size={13} />,
  Series: <Tv size={13} />,
  Episode: <MonitorPlay size={13} />,
  Music: <Mic2 size={13} />,
  Other: null,
}

const TABS: { label: string; value: MediaType | 'All' }[] = [
  { label: 'All', value: 'All' },
  { label: 'Movies', value: 'Movie' },
  { label: 'Series', value: 'Series' },
  { label: 'Episodes', value: 'Episode' },
  { label: 'Music', value: 'Music' },
  { label: 'Other', value: 'Other' },
]

function formatBytes(bytes: number): string {
  if (bytes < 1024 ** 2) return `${(bytes / 1024).toFixed(1)} KB`
  if (bytes < 1024 ** 3) return `${(bytes / 1024 ** 2).toFixed(1)} MB`
  return `${(bytes / 1024 ** 3).toFixed(2)} GB`
}

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
  const [search, setSearch] = useState('')
  const [debouncedSearch, setDebouncedSearch] = useState('')
  const [activeTab, setActiveTab] = useState<MediaType | 'All'>('All')
  const [page, setPage] = useState(1)

  function handleSearch(value: string) {
    setSearch(value)
    setPage(1)
    clearTimeout((window as any)._searchTimer)
    ;(window as any)._searchTimer = setTimeout(() => setDebouncedSearch(value), 300)
  }

  function handleTab(tab: MediaType | 'All') {
    setActiveTab(tab)
    setPage(1)
  }

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

      {/* Tabs */}
      <div className="flex flex-wrap gap-1 mb-4 p-1 bg-[var(--color-surface)] border border-[var(--color-border)] rounded-xl w-fit">
        {TABS.map(tab => {
          const count = counts?.[tab.value]
          if (count === 0 && tab.value !== 'All') return null
          return (
            <button
              key={tab.value}
              onClick={() => handleTab(tab.value)}
              className={`flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-sm font-medium transition-colors cursor-pointer ${
                activeTab === tab.value
                  ? 'bg-[var(--color-accent-dim)] text-[var(--color-accent)]'
                  : 'text-[var(--color-text)] hover:bg-white/5 hover:text-[var(--color-heading)]'
              }`}
            >
              {tab.label}
              {count !== undefined && (
                <span className={`text-xs px-1.5 py-0.5 rounded-md ${
                  activeTab === tab.value
                    ? 'bg-[var(--color-accent)]/20 text-[var(--color-accent)]'
                    : 'bg-white/5 text-[var(--color-text)]'
                }`}>
                  {count}
                </span>
              )}
            </button>
          )
        })}
      </div>

      {/* Search */}
      <div className="relative mb-4">
        <Search size={15} className="absolute left-3 top-1/2 -translate-y-1/2 text-[var(--color-text)]" />
        <input
          value={search}
          onChange={e => handleSearch(e.target.value)}
          placeholder="Search titles…"
          className="w-full bg-[var(--color-surface)] border border-[var(--color-border)] rounded-lg pl-9 pr-4 py-2.5 text-sm text-[var(--color-heading)] placeholder:text-[var(--color-text)] outline-none focus:ring-2 focus:ring-[var(--color-accent)]/40 transition"
        />
      </div>

      {isLoading && (
        <div className="space-y-2">
          {Array.from({ length: 8 }).map((_, i) => (
            <div key={i} className="h-14 bg-[var(--color-surface)] border border-[var(--color-border)] rounded-xl animate-pulse" />
          ))}
        </div>
      )}

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
                  {typeIcon[item.type] ?? <Film size={13} />}
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

          {/* Pagination */}
          {totalPages > 1 && (
            <div className="flex items-center justify-between pt-2">
              <span className="text-xs text-[var(--color-text)]">
                Page {page} of {totalPages} · {totalForTab} items
              </span>
              <div className="flex items-center gap-1">
                <button
                  onClick={() => setPage(p => Math.max(1, p - 1))}
                  disabled={page === 1}
                  className="p-1.5 rounded-lg text-[var(--color-text)] hover:bg-white/5 disabled:opacity-30 disabled:cursor-not-allowed transition-colors cursor-pointer"
                >
                  <ChevronLeft size={16} />
                </button>
                {Array.from({ length: Math.min(7, totalPages) }, (_, i) => {
                  const p = totalPages <= 7
                    ? i + 1
                    : page <= 4 ? i + 1
                    : page >= totalPages - 3 ? totalPages - 6 + i
                    : page - 3 + i
                  return (
                    <button
                      key={p}
                      onClick={() => setPage(p)}
                      className={`min-w-[2rem] h-8 px-2 rounded-lg text-xs font-medium transition-colors cursor-pointer ${
                        p === page
                          ? 'bg-[var(--color-accent-dim)] text-[var(--color-accent)]'
                          : 'text-[var(--color-text)] hover:bg-white/5'
                      }`}
                    >
                      {p}
                    </button>
                  )
                })}
                <button
                  onClick={() => setPage(p => Math.min(totalPages, p + 1))}
                  disabled={page === totalPages}
                  className="p-1.5 rounded-lg text-[var(--color-text)] hover:bg-white/5 disabled:opacity-30 disabled:cursor-not-allowed transition-colors cursor-pointer"
                >
                  <ChevronRight size={16} />
                </button>
              </div>
            </div>
          )}
        </>
      )}
    </div>
  )
}
