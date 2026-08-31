// Time rendering shared by the queue and the player bar.

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
