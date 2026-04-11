import { ChevronLeft, ChevronRight } from 'lucide-react'

interface PaginatorProps {
  page: number
  totalPages: number
  totalItems: number
  onPageChange: (page: number) => void
}

export function Paginator({ page, totalPages, totalItems, onPageChange }: PaginatorProps) {
  if (totalPages <= 1) return null

  return (
    <div className="flex items-center justify-between pt-2">
      <span className="text-xs text-[var(--color-text)]">
        Page {page} of {totalPages} · {totalItems} items
      </span>
      <div className="flex items-center gap-1">
        <button
          onClick={() => onPageChange(Math.max(1, page - 1))}
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
              onClick={() => onPageChange(p)}
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
          onClick={() => onPageChange(Math.min(totalPages, page + 1))}
          disabled={page === totalPages}
          className="p-1.5 rounded-lg text-[var(--color-text)] hover:bg-white/5 disabled:opacity-30 disabled:cursor-not-allowed transition-colors cursor-pointer"
        >
          <ChevronRight size={16} />
        </button>
      </div>
    </div>
  )
}
