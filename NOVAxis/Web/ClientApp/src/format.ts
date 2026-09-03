// Time rendering shared by the queue and the player bar.

import type { PlayerStateDto, QueueItemDto } from './api'

export function formatDuration(ms: number, isLiveStream = false): string {
  if (isLiveStream || ms <= 0) return '∞'

  const total = Math.floor(ms / 1000)
  const hours = Math.floor(total / 3600)
  const minutes = Math.floor((total % 3600) / 60)
  const seconds = total % 60

  const mm = hours > 0 ? String(minutes).padStart(2, '0') : String(minutes)
  const ss = String(seconds).padStart(2, '0')

  return hours > 0 ? `${hours}:${mm}:${ss}` : `${mm}:${ss}`
}

export function formatTotal(ms: number): string {
  const total = Math.floor(ms / 1000)
  const hours = Math.floor(total / 3600)
  const minutes = Math.round((total % 3600) / 60)

  return hours > 0 ? `${hours} h ${minutes} min` : `${minutes} min`
}

export function formatBytes(bytes: number | null): string {
  if (bytes === null || bytes <= 0) return '—'

  if (bytes >= 1024 * 1024 * 1024)
    return `${(bytes / 1024 / 1024 / 1024).toFixed(1).replace('.', ',')} GB`

  if (bytes >= 1024 * 1024)
    return `${(bytes / 1024 / 1024).toFixed(1).replace('.', ',')} MB`

  return `${Math.round(bytes / 1024)} kB`
}

/**
 * When the queue will run out, as a wall clock time - or nothing at all where that
 * cannot be known: a repeat keeps it going forever, a live stream has no length, and
 * a paused player would only drift further from whatever was shown.
 */
export function formatQueueEndTime(state: PlayerStateDto, queueTotalMs: number): string | null {
  if (state.repeatMode !== 'None') return null
  if (state.isPaused || state.state !== 'Playing') return null
  if (!state.current && state.queue.length === 0) return null

  const endless = (item: QueueItemDto) => item.track.isLiveStream || item.track.durationMs <= 0

  if (state.current && endless(state.current)) return null
  if (state.queue.some(endless)) return null

  const remaining = state.current
    ? queueTotalMs + state.current.track.durationMs - state.positionMs
    : queueTotalMs

  // Rounded to the minute, so the value does not tick while nothing meaningful changes
  const end = new Date(Date.now() + Math.round(remaining / 60000) * 60000)

  return `skončí ${end.toLocaleTimeString(undefined, {
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  })}`
}
