import { useRef, useState } from 'react'
import type { MediaType } from '../api/types'

export const TABS: { label: string; value: MediaType | 'All' }[] = [
  { label: 'All', value: 'All' },
  { label: 'Movies', value: 'Movie' },
  { label: 'Series', value: 'Series' },
  { label: 'Episodes', value: 'Episode' },
  { label: 'Music', value: 'Music' },
  { label: 'Other', value: 'Other' },
]

export function useMediaFilter() {
  const [search, setSearch] = useState('')
  const [debouncedSearch, setDebouncedSearch] = useState('')
  const [activeTab, setActiveTab] = useState<MediaType | 'All'>('All')
  const [page, setPage] = useState(1)
  const searchTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  function handleSearch(value: string) {
    setSearch(value)
    setPage(1)
    if (searchTimerRef.current) clearTimeout(searchTimerRef.current)
    searchTimerRef.current = setTimeout(() => setDebouncedSearch(value), 300)
  }

  function handleTab(tab: MediaType | 'All') {
    setActiveTab(tab)
    setPage(1)
  }

  return { search, debouncedSearch, activeTab, page, setPage, handleSearch, handleTab }
}
