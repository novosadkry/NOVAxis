import { useCallback, useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'

import { api, GuildDto, TrackDto } from '../api'
import { useDownload, usePlayerState } from '../live'
import { useUser } from '../user'
import { useToast } from '../Toast'
import { Download, Power } from '../Icons'
import { describeFailure } from './DownloadsPage'
import { NowPlaying } from '../components/NowPlaying'
import { PlayerBar } from '../components/PlayerBar'
import { QueueList } from '../components/QueueList'
import { SearchBox } from '../components/SearchBox'

/**
 * The player itself: sidebar with the guilds, the now-playing hero, the queue
 * and the transport bar. Everything on this page redraws from one live state.
 */
export function PlayerPage() {
  const { guildId = '' } = useParams()
  const user = useUser()
  const live = usePlayerState(guildId)
  const [guilds, setGuilds] = useState<GuildDto[]>([])

  // One place watches the download, so the rows and the card do not each poll for it
  const { overview, reload } = useDownload()
  const { toast } = useToast()
  const [starting, setStarting] = useState<string | null>(null)

  const active = overview?.active ?? null

  const quickDownload = useCallback(
    async (track: TrackDto) => {
      if (!track.uri || starting) return

      // Only one link lives at a time, so say so rather than quietly taking the last one
      const replacing = active !== null && (active.state === 'Ready' || active.state === 'Running' || active.state === 'Pending')

      setStarting(track.uri)

      try {
        await api.startDownload(track.uri, 'Audio', '', track.title)
        reload()
        toast(
          replacing
            ? `Stahuji „${track.title}“ — předchozí odkaz byl nahrazen`
            : `Stahuji „${track.title}“`,
        )
      } catch (error) {
        toast(describeFailure(error))
      } finally {
        setStarting(null)
      }
    },
    [active, reload, starting, toast],
  )

  useEffect(() => {
    api.guilds().then(setGuilds).catch(() => setGuilds([]))
  }, [])

  const guild = guilds.find(g => g.id === guildId)
  const state = live.state

  return (
    <div className="app">
      <aside className="sidebar">
        <Link to="/" className="brand">
          <span className="brand-ring" aria-hidden="true" />
          <span className="brand-name">NOVAXIS</span>
        </Link>
        <p className="sidebar-label">Servery</p>
        <nav className="sidebar-guilds">
          {guilds.map(g => (
            <Link
              key={g.id}
              to={`/g/${g.id}`}
              className={`sidebar-guild${g.id === guildId ? ' active' : ''}`}
            >
              {g.iconUrl ? (
                <img src={g.iconUrl} alt="" />
              ) : (
                <span className="guild-icon-fallback">{g.name.slice(0, 1).toUpperCase()}</span>
              )}
              <span className="sidebar-guild-name">{g.name}</span>
              {g.connected && <span className="dot" aria-label="hraje" />}
            </Link>
          ))}
        </nav>
        <p className="sidebar-label">Nástroje</p>
        <Link to="/downloads" className="sidebar-guild">
          <span className="guild-icon-fallback">
            <Download size={16} />
          </span>
          <span className="sidebar-guild-name">Stahování</span>
          {active && active.state !== 'Failed' && (
            <span className={`download-badge${active.state === 'Ready' ? ' ready' : ''}`}>
              {active.state === 'Ready'
                ? 'hotovo'
                : active.progress !== null
                  ? `${Math.round(active.progress * 100)} %`
                  : '…'}
            </span>
          )}
          {active?.state === 'Failed' && <span className="download-badge failed">chyba</span>}
        </Link>
        {user && (
          <div className="sidebar-user">
            {user.avatarUrl && <img src={user.avatarUrl} alt="" />}
            <span>{user.name}</span>
            <button
              type="button"
              className="icon-btn"
              title="Odhlásit se"
              aria-label="Odhlásit se"
              onClick={() => api.logout().then(() => window.location.assign('/'))}
            >
              <Power size={16} />
            </button>
          </div>
        )}
      </aside>

      <main className="main">
        <header className="topbar">
          <div className="topbar-title">
            <Link to="/" className="topbar-back" aria-label="Zpět na výběr serveru">
              ‹
            </Link>
            <div>
              <h1>{guild?.name ?? '…'}</h1>
              <p className="topbar-sub">
                {state?.connected && state.voiceChannel
                  ? `připojeno · ${state.voiceChannel.name}`
                  : 'jádro v klidovém režimu'}
              </p>
            </div>
          </div>
          <div className="topbar-actions">
            <SearchBox guildId={guildId} />
            {/* Also here, not only in the sidebar: that is hidden on a narrow screen,
                which would leave the player with no way through to a download at all */}
            <Link to="/downloads" className="btn-ghost topbar-downloads">
              <Download size={16} />
              <span>Stahování</span>
              {active && active.state !== 'Failed' && (
                <span className={`download-badge${active.state === 'Ready' ? ' ready' : ''}`}>
                  {active.state === 'Ready'
                    ? 'hotovo'
                    : active.progress !== null
                      ? `${Math.round(active.progress * 100)} %`
                      : '…'}
                </span>
              )}
            </Link>
          </div>
        </header>

        {live.error && <p className="empty-note">{live.error}</p>}

        {!live.error && (
          <>
            <NowPlaying live={live} onDownload={quickDownload} startingUri={starting} />
            <QueueList
              guildId={guildId}
              state={state}
              onDownload={quickDownload}
              startingUri={starting}
            />
          </>
        )}
      </main>

      <PlayerBar guildId={guildId} live={live} />
    </div>
  )
}
