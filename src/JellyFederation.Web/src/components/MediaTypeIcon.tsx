import { Film, Mic2, MonitorPlay, Tv } from 'lucide-react'
import type { MediaType } from '../api/types'

interface MediaTypeIconProps {
  type: MediaType
  size?: number
}

export function MediaTypeIcon({ type, size = 13 }: MediaTypeIconProps) {
  switch (type) {
    case 'Movie':
      return <Film size={size} />
    case 'Series':
      return <Tv size={size} />
    case 'Episode':
      return <MonitorPlay size={size} />
    case 'Music':
      return <Mic2 size={size} />
    case 'Other':
      return <Film size={size} />
  }
}
