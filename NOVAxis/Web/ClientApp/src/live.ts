import { useCallback, useEffect, useRef, useState } from 'react'

import { api, ApiError, DownloadOverviewDto, PlayerStateDto } from './api'

export interface LiveState {
  state: PlayerStateDto | null
  /** Client clock at the moment the snapshot arrived - the interpolation base. */
  receivedAt: number
  error: string | null
}

// Polling ticks every 3s - anything twice that stale is not a slow tick, it is a link
// that stopped delivering, and extrapolating past it would just be inventing a position
const STALE_AFTER_MS = 8000

/**
 * The position to draw right now - the last sample plus the time elapsed since
 * it arrived, frozen while paused and clamped to the track's length. Once the snapshot
 * itself is stale, elapsed time stops being trusted and the bar holds instead of drifting
 * further past a truth nobody has confirmed.
 */
export function usePosition(live: LiveState): number {
  const [, setFrame] = useState(0)
  const raf = useRef<number>()

  const playing = live.state?.state === 'Playing' && !live.state.isPaused

  useEffect(() => {
    if (!playing) return

    let last = 0

    const loop = (time: number) => {
      // Four frames a second is plenty for a progress bar
      if (time - last > 250) {
        last = time
        setFrame(f => f + 1)
      }

      raf.current = requestAnimationFrame(loop)
    }

    raf.current = requestAnimationFrame(loop)

    return () => {
      if (raf.current) cancelAnimationFrame(raf.current)
    }
  }, [playing])

  const state = live.state

  if (!state?.current) return 0
  if (!playing) return state.positionMs

  const age = Date.now() - live.receivedAt
  const elapsed = Math.max(Math.min(age, STALE_AFTER_MS), 0)
  const position = state.positionMs + elapsed
  const duration = state.current.track.durationMs

  return duration > 0 ? Math.min(position, duration) : position
}

export interface DownloadLive {
  overview: DownloadOverviewDto | null
  error: string | null
  reload: () => void
}

/**
 * Watches the caller's one download slot. Polled rather than pushed: the hub is keyed by
 * guild and this page has none, so a socket here would exist to carry a single message.
 * The interval follows the work - a second while something is running, a quarter minute
 * once it is ready, and nothing at all while the slot is empty.
 */
export function useDownload(): DownloadLive {
  const [overview, setOverview] = useState<DownloadOverviewDto | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [nonce, setNonce] = useState(0)

  const reload = useCallback(() => setNonce(n => n + 1), [])

  const held = overview?.downloads ?? []
  const running = held.some(d => d.state === 'Pending' || d.state === 'Running')

  const interval = running ? 1000 : held.length > 0 ? 15000 : 0

  useEffect(() => {
    let disposed = false
    let timer: number | undefined

    const tick = () =>
      api
        .downloads()
        .then(next => {
          if (disposed) return
          setOverview(next)
          setError(null)
        })
        .catch(failure => {
          if (disposed) return
          // The last good snapshot stays on screen - a blip should not blank the page
          setError(failure instanceof ApiError ? failure.message : 'Nastala neznámá chyba')
        })

    tick()

    if (interval > 0) {
      // A background tab must not keep a yt-dlp status endpoint busy
      const start = () => {
        if (timer === undefined && !document.hidden) timer = window.setInterval(tick, interval)
      }

      const stop = () => {
        if (timer !== undefined) {
          window.clearInterval(timer)
          timer = undefined
        }
      }

      const onVisibility = () => {
        if (document.hidden) stop()
        else {
          tick()
          start()
        }
      }

      start()
      document.addEventListener('visibilitychange', onVisibility)

      return () => {
        disposed = true
        stop()
        document.removeEventListener('visibilitychange', onVisibility)
      }
    }

    return () => {
      disposed = true
    }
  }, [interval, nonce])

  return { overview, error, reload }
}
