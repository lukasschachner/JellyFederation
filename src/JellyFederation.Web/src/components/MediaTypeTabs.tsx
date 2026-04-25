import type { MediaType } from '../api/types'
import { TABS } from '../hooks/useMediaFilter'

type MediaFilterTab = MediaType | 'All'

interface MediaTypeTabsProps {
  activeTab: MediaFilterTab
  counts?: Record<string, number>
  onTabChange: (tab: MediaFilterTab) => void
}

export function MediaTypeTabs({ activeTab, counts, onTabChange }: MediaTypeTabsProps) {
  return (
    <div className="flex flex-wrap gap-1 mb-4 p-1 bg-[var(--color-surface)] border border-[var(--color-border)] rounded-xl w-fit">
      {TABS.map(tab => {
        const count = counts?.[tab.value]
        if (count === 0 && tab.value !== 'All') return null
        const isActive = activeTab === tab.value

        return (
          <button
            key={tab.value}
            type="button"
            aria-pressed={isActive}
            onClick={() => onTabChange(tab.value)}
            className={`flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-sm font-medium transition-colors cursor-pointer ${
              isActive
                ? 'bg-[var(--color-accent-dim)] text-[var(--color-accent)]'
                : 'text-[var(--color-text)] hover:bg-white/5 hover:text-[var(--color-heading)]'
            }`}
          >
            {tab.label}
            {count !== undefined && (
              <span className={`text-xs px-1.5 py-0.5 rounded-md ${
                isActive
                  ? 'bg-[var(--color-accent)]/20 text-[var(--color-accent)]'
                  : 'bg-white/5 text-[var(--color-text)]'
              }`}
              >
                {count}
              </span>
            )}
          </button>
        )
      })}
    </div>
  )
}
