import { Link } from 'react-router-dom'

import { useGuilds } from '../guilds'
import { usePlayerState } from '../player'
import { Download, Note } from '../Icons'

/**
 * The landing view - every guild the user and the bot share, with the ones
 * playing right now marked. Picking one opens its player.
 */
export function GuildPicker() {
  const { guilds, loading } = useGuilds()

  // The connection outlives the page now, so the picker has to say it follows nobody -
  // otherwise the server keeps ticking out snapshots for whichever guild was last open
  usePlayerState(null)

  return (
    <div className="screen picker">
      <header className="picker-head">
        <p className="eyebrow">NOVAXIS · PŘEHRÁVAČ</p>
        <h1>Vyber server</h1>
        <Link className="text-btn" to="/downloads">
          <Download size={14} /> Stahování videí a hudby
        </Link>
      </header>

      {loading && <div className="pulse-ring" aria-label="Načítání" />}

      {!loading && guilds.length === 0 && (
        <p className="empty-note">
          Nesdílíš s&nbsp;jádrem žádný server. Pozvi bota, nebo se připoj tam, kde už je.
        </p>
      )}

      <div className="guild-grid">
        {guilds.map(guild => (
          <Link key={guild.id} to={`/g/${guild.id}`} className="guild-card">
            {guild.iconUrl ? (
              <img src={guild.iconUrl} alt="" className="guild-icon" />
            ) : (
              <div className="guild-icon guild-icon-fallback" aria-hidden="true">
                {guild.name.slice(0, 1).toUpperCase()}
              </div>
            )}
            <div className="guild-meta">
              <span className="guild-name">{guild.name}</span>
              {guild.connected && (
                <span className="live-chip">
                  <Note size={12} /> hraje
                </span>
              )}
            </div>
          </Link>
        ))}
      </div>
    </div>
  )
}
