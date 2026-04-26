interface MediaGridSkeletonProps {
  count?: number
}

function SkeletonBlock({ className = '' }: { className?: string }) {
  return <div className={`rounded bg-white/[0.06] ${className}`} />
}

export function MediaGridSkeleton({ count = 8 }: MediaGridSkeletonProps) {
  return (
    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
      {Array.from({ length: count }).map((_, i) => (
        <div key={i} className="overflow-hidden rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] shadow-sm shadow-black/20 animate-pulse">
          <SkeletonBlock className="aspect-video rounded-none" />
          <div className="space-y-3 p-4">
            <SkeletonBlock className="h-4 w-4/5" />
            <div className="flex gap-2">
              <SkeletonBlock className="h-5 w-14 rounded-full" />
              <SkeletonBlock className="h-5 w-10 rounded-full" />
            </div>
            <SkeletonBlock className="h-3 w-full" />
            <SkeletonBlock className="h-3 w-2/3" />
            <div className="flex items-center justify-between border-t border-[var(--color-border)] pt-3">
              <SkeletonBlock className="h-3 w-24" />
              <SkeletonBlock className="h-8 w-20 rounded-lg" />
            </div>
          </div>
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
        <div key={i} className="flex items-center gap-4 rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] px-4 py-3 shadow-sm shadow-black/20 animate-pulse">
          <SkeletonBlock className="h-8 w-8 rounded-lg" />
          <div className="min-w-0 flex-1 space-y-2">
            <SkeletonBlock className="h-4 w-2/3" />
            <SkeletonBlock className="h-3 w-1/3" />
          </div>
          <SkeletonBlock className="hidden h-4 w-16 sm:block" />
          <SkeletonBlock className="h-5 w-9 rounded-full" />
        </div>
      ))}
    </div>
  )
}
