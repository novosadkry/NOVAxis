import { useRef, useState } from 'react'

import { api, PlayerStateDto } from '../api'
import { formatDuration } from '../format'
import { LiveState, usePosition } from '../live'
import { Pause, Play, Power, Repeat, RepeatOne, Skip, Stop, Volume } from '../Icons'
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

/**
 * The transport - the one place every control lives. The progress line across
 * its top edge is scrubbable and doubles as the page's heartbeat.
 */
export function PlayerBar({ guildId, live }: PlayerBarProps) {
  const { run } = useToast()
  const state = live.state
  const position = usePosition(live)
  const lineRef = useRef<HTMLDivElement>(null)

  const [pendingVolume, setPendingVolume] = useState<number | null>(null)
  const volumeTimer = useRef<number>()

  const track = state?.current?.track
  const playing = state?.state === 'Playing' && !state.isPaused
  const seekable = !!track && !track.isLiveStream && track.durationMs > 0
  const progress = seekable ? Math.min(position / track.durationMs, 1) : 0

  const seek = (event: React.MouseEvent) => {
    if (!seekable || !lineRef.current) return

    const rect = lineRef.current.getBoundingClientRect()
    const ratio = Math.min(Math.max((event.clientX - rect.left) / rect.width, 0), 1)

    run(api.seek(guildId, Math.floor(ratio * track.durationMs)))
  }

  const volume = pendingVolume ?? Math.round((state?.volume ?? 1) * 100)

  const changeVolume = (percent: number) => {
    setPendingVolume(percent)

    // One request when the slider settles, not one per pixel
    window.clearTimeout(volumeTimer.current)
    volumeTimer.current = window.setTimeout(() => {
      run(api.volume(guildId, percent))
      setPendingVolume(null)
    }, 250)
  }

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
            className={'icon-btn' + (state?.repeatMode !== 'None' ? ' active' : '')}
            aria-label="Režim opakování"
            title={`Opakování: ${state?.repeatMode ?? 'None'}`}
            disabled={disabled}
            onClick={() => state && run(api.repeat(guildId, repeatCycle[state.repeatMode]))}
          >
            {state?.repeatMode === 'Track' ? <RepeatOne size={18} /> : <Repeat size={18} />}
          </button>

          <button
            type="button"
            className="play-btn"
            aria-label={playing ? 'Pozastavit' : 'Přehrát'}
            disabled={disabled}
            onClick={() => run(playing ? api.pause(guildId) : api.resume(guildId))}
          >
            {playing ? <Pause size={22} /> : <Play size={22} />}
          </button>

          <button
            type="button"
            className="icon-btn"
            aria-label="Přeskočit"
            disabled={disabled}
            onClick={() => run(api.skip(guildId))}
          >
            <Skip size={18} />
          </button>

          <button
            type="button"
            className="icon-btn"
            aria-label="Zastavit a vyprázdnit frontu"
            title="Zastavit a vyprázdnit frontu"
            disabled={disabled}
            onClick={() => run(api.stop(guildId))}
          >
            <Stop size={18} />
          </button>
        </div>

        <div className="playerbar-right">
          <label className="volume">
            <Volume size={18} />
            <input
              type="range"
              min={0}
              max={150}
              value={volume}
              disabled={!state?.connected}
              aria-label="Hlasitost"
              onChange={e => changeVolume(Number(e.target.value))}
            />
            <span className="volume-value">{volume}%</span>
          </label>

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
