import { Link } from 'react-router-dom'

import { LiveState } from '../live'
import { Download, Note } from '../Icons'
import { downloadHref } from '../pages/DownloadsPage'

/**
 * The hero - what the core is playing right now, with who asked for it.
 * When nothing plays, it turns into a quiet invitation instead.
 */
export function NowPlaying({ live }: { live: LiveState }) {
  const state = live.state
  const item = state?.current

  if (!state || !state.connected || !item) {
    return (
      <section className="hero hero-idle">
        <div className="hero-idle-ring" aria-hidden="true" />
        <div>
          <p className="eyebrow">KLIDOVÝ REŽIM</p>
          <h2>Nic právě nehraje</h2>
          <p className="hero-idle-sub">
            Připoj se na Discordu do hlasového kanálu a vyhledej první skladbu —
            jádro se přidá za tebou.
          </p>
        </div>
      </section>
    )
  }

  const track = item.track

  return (
    <section className="hero">
      <div className="hero-art">
        {track.artworkUri ? (
          <img src={track.artworkUri} alt="" />
        ) : (
          <div className="hero-art-fallback">
            <Note size={48} />
          </div>
        )}
      </div>

      <div className="hero-meta">
        <p className="eyebrow accent">
          {state.isPaused ? 'POZASTAVENO' : 'PRÁVĚ HRAJE'}
          {track.isLiveStream && <span className="live-badge">ŽIVĚ</span>}
        </p>
        <h2 className="hero-title">
          {track.uri ? (
            <a href={track.uri} target="_blank" rel="noreferrer">
              {track.title}
            </a>
          ) : (
            track.title
          )}
        </h2>
        {track.author && <p className="hero-author">{track.author}</p>}

        {item.requestedBy && (
          <div className="requester">
            {item.requestedBy.avatarUrl && <img src={item.requestedBy.avatarUrl} alt="" />}
            <span>
              vyžádal <strong>{item.requestedBy.name}</strong>
            </span>
          </div>
        )}

        {/* A live stream has no end, so it would only ever hit the size ceiling */}
        {track.uri && !track.isLiveStream && (
          <div className="hero-actions">
            <Link className="btn-ghost" to={downloadHref(track.uri, `/g/${state.guildId}`)}>
              <Download size={16} /> Stáhnout
            </Link>
          </div>
        )}
      </div>
    </section>
  )
}
