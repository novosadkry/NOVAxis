import { useEffect, useRef, useState } from 'react'

import { api, PlayerStateDto } from '../api'
import { formatDuration } from '../format'
import { LiveState, usePosition } from '../live'
import { Pause, Play, Power, Repeat, RepeatOne, Skip, Stop, Volume, VolumeMuted } from '../Icons'
import { useToast } from '../Toast'

interface PlayerBarProps {
  guildId: string
  live: LiveState
}

const repeatCycle: Record<PlayerStateDto['repeatMode'], PlayerStateDto['repeatMode']> = {
  None: 'Queue',
  Queue: 'Track',
  Track: 'None',
}

const repeatNames: Record<PlayerStateDto['repeatMode'], string> = {
  None: 'vypnuto',
  Queue: 'celá fronta',
  Track: 'jedna skladba',
}

interface Pending<T> {
  value: T
  /**
   * Client clock at which the request came back. A snapshot older than this was already on
   * the wire when the control was touched, so it still carries the value being replaced.
   */
  ackAt: number
}

/**
 * A control that answers the click instead of the network: the chosen value shows at once
 * and holds until the server is seen agreeing with it. Over the three-second polling
 * fallback the alternative is a button that looks broken.
 *
 * `delay` coalesces a burst of changes into one request - the slider's need.
 */
function useOptimistic<T>(receivedAt: number, send: (value: T) => Promise<unknown>, delay = 0) {
  const { run } = useToast()
  const [pending, setPending] = useState<Pending<T> | null>(null)
  const timers = useRef<{ send?: number; expiry?: number }>({})
  const attempt = useRef(0)

  const clearTimers = () => {
    window.clearTimeout(timers.current.send)
    window.clearTimeout(timers.current.expiry)
  }

  useEffect(() => {
    // Waiting for the acknowledgement first is what keeps a snapshot that predates the
    // command from flicking the control back to the old value and then forward again
    if (pending && receivedAt > pending.ackAt) {
      clearTimers()
      setPending(null)
    }
  }, [pending, receivedAt])

  useEffect(() => clearTimers, [])

  const apply = (value: T) => {
    const attemptId = ++attempt.current

    clearTimers()
    setPending({ value, ackAt: Infinity })

    timers.current.send = window.setTimeout(() => {
      const request = send(value)

      request.then(
        () => {
          // A later touch owns the override by now, and its own answer is still coming
          if (attemptId === attempt.current)
            setPending(current => (current ? { ...current, ackAt: Date.now() } : null))
        },
        () => {
          // run() turns the failure into a toast; this only has to stop the UI lying
          if (attemptId === attempt.current) setPending(null)
        },
      )

      run(request)

      // A command that is lost or ignored must not leave a wrong value on screen for good
      timers.current.expiry = window.setTimeout(() => setPending(null), 5000)
    }, delay)
  }

  return [pending?.value, apply] as const
}

/**
 * For the commands with no honest local answer - which track comes next is the server's to
 * decide. All this can do is keep an impatient second click from skipping a second track.
 */
function useSingleFlight(send: () => Promise<unknown>) {
  const { run } = useToast()
  const [busy, setBusy] = useState(false)

  const fire = () => {
    setBusy(true)
    run(send()).then(() => setBusy(false))
  }

  return [busy, fire] as const
}

/**
 * The transport - the one place every control lives. The progress line across
 * its top edge is scrubbable and doubles as the page's heartbeat.
 */
export function PlayerBar({ guildId, live }: PlayerBarProps) {
  const { run } = useToast()
  const state = live.state
  const position = usePosition(live)
  const lineRef = useRef<HTMLDivElement>(null)

  const [pendingPlaying, setPlaying] = useOptimistic<boolean>(live.receivedAt, next =>
    next ? api.resume(guildId) : api.pause(guildId),
  )

  const [pendingRepeat, setRepeat] = useOptimistic<PlayerStateDto['repeatMode']>(
    live.receivedAt,
    mode => api.repeat(guildId, mode),
  )

  // One request when the slider settles, not one per pixel
  const [pendingVolume, setVolume] = useOptimistic<number>(
    live.receivedAt,
    percent => api.volume(guildId, percent),
    250,
  )

  const [skipping, skip] = useSingleFlight(() => api.skip(guildId))
  const [stopping, stopAll] = useSingleFlight(() => api.stop(guildId))

  const track = state?.current?.track
  const playing = pendingPlaying ?? (state?.state === 'Playing' && !state.isPaused)
  const repeatMode = pendingRepeat ?? state?.repeatMode ?? 'None'
  const seekable = !!track && !track.isLiveStream && track.durationMs > 0
  const progress = seekable ? Math.min(position / track.durationMs, 1) : 0

  const seek = (event: React.MouseEvent) => {
    if (!seekable || !lineRef.current) return

    const rect = lineRef.current.getBoundingClientRect()
    const ratio = Math.min(Math.max((event.clientX - rect.left) / rect.width, 0), 1)

    run(api.seek(guildId, Math.floor(ratio * track.durationMs)))
  }

  const volume = pendingVolume ?? Math.round((state?.volume ?? 1) * 100)
  const muted = volume === 0
  const restore = useRef(100)

  useEffect(() => {
    // Unmuting returns the slider to wherever it last stood, or to normal if the player
    // has been silent since the page loaded
    if (volume > 0) restore.current = volume
  }, [volume])

  const disabled = !state?.connected || !state.current

  return (
    <footer className="playerbar">
      <div
        ref={lineRef}
        className={'progress-line' + (seekable ? ' seekable' : '')}
        onClick={seek}
        role={seekable ? 'slider' : undefined}
        aria-label={seekable ? 'Pozice ve skladbě' : undefined}
        aria-valuemin={0}
        aria-valuemax={track?.durationMs ?? 0}
        aria-valuenow={Math.floor(position)}
      >
        <div className="progress-fill" style={{ width: `${progress * 100}%` }} />
      </div>

      <div className="playerbar-inner">
        <div className="playerbar-times">
          <span className="time-now">{track ? formatDuration(position) : '–:––'}</span>
          <span className="time-sep">/</span>
          <span className="time-total">
            {track ? formatDuration(track.durationMs, track.isLiveStream) : '–:––'}
          </span>
        </div>

        <div className="playerbar-controls">
          <button
            type="button"
            className={'icon-btn' + (repeatMode !== 'None' ? ' active' : '')}
            aria-label="Režim opakování"
            title={`Opakování: ${repeatNames[repeatMode]}`}
            disabled={disabled}
            onClick={() => setRepeat(repeatCycle[repeatMode])}
          >
            {repeatMode === 'Track' ? <RepeatOne size={18} /> : <Repeat size={18} />}
          </button>

          <button
            type="button"
            className="play-btn"
            aria-label={playing ? 'Pozastavit' : 'Přehrát'}
            disabled={disabled}
            onClick={() => setPlaying(!playing)}
          >
            {playing ? <Pause size={22} /> : <Play size={22} />}
          </button>

          <button
            type="button"
            className="icon-btn"
            aria-label="Přeskočit"
            disabled={disabled || skipping}
            onClick={skip}
          >
            <Skip size={18} />
          </button>

          <button
            type="button"
            className="icon-btn"
            aria-label="Zastavit a vyprázdnit frontu"
            title="Zastavit a vyprázdnit frontu"
            disabled={disabled || stopping}
            onClick={stopAll}
          >
            <Stop size={18} />
          </button>
        </div>

        <div className="playerbar-right">
          <div className="volume">
            <button
              type="button"
              className="icon-btn volume-btn"
              aria-label={muted ? 'Zrušit ztlumení' : 'Ztlumit'}
              title={muted ? 'Zrušit ztlumení' : 'Ztlumit'}
              disabled={!state?.connected}
              onClick={() => setVolume(muted ? restore.current : 0)}
            >
              {muted ? <VolumeMuted size={18} /> : <Volume size={18} />}
            </button>

            <span className="volume-slider">
              <input
                type="range"
                min={0}
                max={150}
                value={volume}
                disabled={!state?.connected}
                aria-label="Hlasitost"
                onChange={e => setVolume(Number(e.target.value))}
              />
            </span>

            <span className="volume-value">{volume}%</span>
          </div>

          <button
            type="button"
            className="icon-btn"
            aria-label="Odpojit jádro z kanálu"
            title="Odpojit z kanálu"
            disabled={!state?.connected}
            onClick={() => run(api.disconnect(guildId))}
          >
            <Power size={18} />
          </button>
        </div>
      </div>
    </footer>
  )
}
