import { createContext, useContext } from 'react'

import { ApiError, DownloadDto } from './api'
import { DownloadLive, useDownload } from './live'

const DownloadContext = createContext<DownloadLive>({
  overview: null,
  error: null,
  reload: () => undefined,
})

/** One watcher for the whole app - the shell, the rows and the panel all read it. */
export function DownloadProvider({ children }: { children: React.ReactNode }) {
  const live = useDownload()
  return <DownloadContext.Provider value={live}>{children}</DownloadContext.Provider>
}

export const useDownloads = () => useContext(DownloadContext)

/** Where a track's download button points when the format is worth choosing. */
export function downloadHref(uri: string, from?: string): string {
  const params = new URLSearchParams({ url: uri })
  if (from) params.set('from', from)
  return `${from ? `${from}/downloads` : '/downloads'}?${params}`
}

/** What the file endpoint redirects back with when a link no longer works. */
const FileErrors: Record<string, string> = {
  expired: 'Odkaz vypršel — spusť stahování znovu',
  revoked: 'Odkaz byl zrušen',
  not_found: 'Soubor už na serveru není',
}

export const describeFileError = (code: string) =>
  FileErrors[code] ?? 'Odkaz se nepodařilo otevřít'

export function describeFailure(error: unknown): string {
  if (!(error instanceof ApiError)) return 'Nastala neznámá chyba'

  // The limiter answers without a code; the hourly quota answers with one
  if (error.status === 429 && error.code === 'rate_limited')
    return 'Moc rychle po sobě — zkus to za chvíli'

  return error.message
}

/** True while a download is worth warning about replacing. */
export const isLive = (active: DownloadDto | null) =>
  active !== null && (active.state === 'Ready' || active.state === 'Running' || active.state === 'Pending')
