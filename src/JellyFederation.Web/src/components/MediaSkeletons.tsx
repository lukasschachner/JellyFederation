interface MediaGridSkeletonProps {
  count?: number
}

export function MediaGridSkeleton({ count = 8 }: MediaGridSkeletonProps) {
  return (
    <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 gap-4">
      {Array.from({ length: count }).map((_, i) => (
        <div key={i} className="bg-[var(--color-surface)] border border-[var(--color-border)] rounded-xl p-5 animate-pulse">
          <div className="w-full aspect-video bg-white/5 rounded-lg mb-3" />
          <div className="h-4 bg-white/5 rounded mb-2" />
          <div className="h-3 bg-white/5 rounded w-2/3" />
        </div>
      ))}
    </div>
  )
}

interface MediaListSkeletonProps {
  count?: number
}

export function MediaListSkeleton({ count = 8 }: MediaListSkeletonProps) {
  return (
    <div className="space-y-2">
      {Array.from({ length: count }).map((_, i) => (
        <div key={i} className="h-14 bg-[var(--color-surface)] border border-[var(--color-border)] rounded-xl animate-pulse" />
      ))}
    </div>
  )
}
