import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'

import { api, GuildDto } from '../api'
import { usePlayerState } from '../live'
import { useUser } from '../user'
import { Download, Power } from '../Icons'
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
          <SearchBox guildId={guildId} />
        </header>

        {live.error && <p className="empty-note">{live.error}</p>}

        {!live.error && (
          <>
            <NowPlaying live={live} />
            <QueueList guildId={guildId} state={state} />
          </>
        )}
      </main>

      <PlayerBar guildId={guildId} live={live} />
    </div>
  )
}
