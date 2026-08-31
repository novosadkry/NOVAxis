import { useCallback, useEffect, useRef, useState } from 'react'
import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr'

import { api, ApiError, DownloadOverviewDto, PlayerStateDto } from './api'

export interface LiveState {
  state: PlayerStateDto | null
  /** Client clock at the moment the snapshot arrived - the interpolation base. */
  receivedAt: number
  error: string | null
}

/**
 * Subscribes to a guild's live state over SignalR, falling back to polling when
 * the socket cannot be established. Every snapshot is stamped with the client
 * clock so the progress bar interpolates without trusting the server clock.
 */
export function usePlayerState(guildId: string): LiveState {
  const [live, setLive] = useState<LiveState>({ state: null, receivedAt: 0, error: null })

  useEffect(() => {
    let disposed = false
    let poller: number | undefined
    let connection: HubConnection | undefined

    const receive = (state: PlayerStateDto) => {
      if (disposed || state.guildId !== guildId) return
      setLive({ state, receivedAt: Date.now(), error: null })
    }

    const fail = (message: string) => {
      if (disposed) return
      setLive(prev => ({ ...prev, error: message }))
    }

    const startPolling = () => {
      if (disposed || poller !== undefined) return

      const tick = () => api.state(guildId).then(receive).catch(() => undefined)

      tick()
      poller = window.setInterval(tick, 3000)
    }

    connection = new HubConnectionBuilder()
      .withUrl('/hub/player')
      .withAutomaticReconnect()
      .build()

    connection.on('state', receive)

    const subscribe = () =>
      connection!
        .invoke<PlayerStateDto>('Subscribe', guildId)
        .then(receive)
        .catch(() => fail('Nejste členem tohoto serveru'))

    connection.onreconnected(() => void subscribe())

    connection
      .start()
      .then(subscribe)
      .catch(startPolling)

    return () => {
      disposed = true

      if (poller !== undefined) window.clearInterval(poller)

      connection?.stop().catch(() => undefined)
    }
  }, [guildId])

  return live
}

/**
 * The position to draw right now - the last sample plus the time elapsed since
 * it arrived, frozen while paused and clamped to the track's length.
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

  const elapsed = Math.max(Date.now() - live.receivedAt, 0)
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

  const status = overview?.active?.state
  const interval = status === 'Pending' || status === 'Running' ? 1000 : status === 'Ready' ? 15000 : 0

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
