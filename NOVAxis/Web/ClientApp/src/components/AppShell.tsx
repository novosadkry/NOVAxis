import { Link } from 'react-router-dom'

import { api } from '../api'
import { headline, useDownloads } from '../downloads'
import { useGuilds } from '../guilds'
import { usePlayerTransport } from '../player'
import { useUser } from '../user'
import { Download, Power } from '../Icons'

interface AppShellProps {
  /** Highlights the guild being looked at, if any. */
  activeGuildId?: string
  /** Highlights the tools entry instead. */
  activeTool?: 'downloads'
  /** Falls back to the name of the guild being looked at. */
  title?: string
  subtitle?: string
  /** The right hand side of the top bar - a search box, say. */
  actions?: React.ReactNode
  /** Where the back arrow leads - the player you came from, or the guild picker. */
  backTo?: string
  /** The transport bar, when there is a guild to control. */
  bar?: React.ReactNode
  children: React.ReactNode
}

/**
 * The frame every signed-in view sits in: the guilds down the side, a heading, and the
 * transport along the bottom. Views change what fills the middle and nothing else, so
 * moving between them is never a change of scenery.
 */
export function AppShell({
  activeGuildId,
  activeTool,
  title,
  subtitle,
  actions,
  backTo = '/',
  bar,
  children,
}: AppShellProps) {
  const user = useUser()
  const { guilds } = useGuilds()
  const { transport, guildId: liveGuildId } = usePlayerTransport()

  const heading = title ?? guilds.find(g => g.id === activeGuildId)?.name ?? '…'

  // Nothing to say about a socket nobody is following (the downloads page has no guild),
  // nothing while it is healthy, and nothing during the first handshake either - that one
  // is expected and quick, and a warning that flashes on every load stops being read
  const linkStatus =
    liveGuildId === null || transport === 'hub' || transport === 'connecting'
      ? null
      : transport === 'polling'
        ? 'omezené spojení · aktualizuji pomaleji'
        : 'spojuji se s jádrem…'

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
              className={`sidebar-guild${g.id === activeGuildId && !activeTool ? ' active' : ''}`}
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
        <Link
          to="/downloads"
          className={`sidebar-guild${activeTool === 'downloads' ? ' active' : ''}`}
        >
          <span className="guild-icon-fallback">
            <Download size={16} />
          </span>
          <span className="sidebar-guild-name">Stahování</span>
          <DownloadBadge />
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
            <Link to={backTo} className="topbar-back" aria-label="Zpět">
              ‹
            </Link>
            <div>
              <h1>{heading}</h1>
              {subtitle && <p className="topbar-sub">{subtitle}</p>}
              {linkStatus && (
                <p
                  className={`topbar-live${transport === 'polling' ? ' topbar-live-fallback' : ''}`}
                  role="status"
                >
                  {linkStatus}
                </p>
              )}
            </div>
          </div>

          <div className={`topbar-actions${actions ? '' : ' compact'}`}>
            {actions}
            {/* The sidebar is gone below 860px, so the way through lives here too */}
            {activeTool !== 'downloads' && (
              <Link
                to="/downloads"
                className="icon-btn topbar-downloads"
                title="Stahování"
                aria-label="Stahování"
              >
                <Download size={18} />
                <DownloadBadge dot />
              </Link>
            )}
          </div>
        </header>

        {children}
      </main>

      {bar}
    </div>
  )
}

/**
 * How the download is getting on, wherever there is room to say. Reduced to a dot where
 * there is not.
 */
function DownloadBadge({ dot = false }: { dot?: boolean }) {
  const { overview } = useDownloads()
  const active = headline(overview?.downloads ?? [])

  if (active === null) return null

  const tone =
    active.state === 'Ready' ? ' ready' : active.state === 'Failed' ? ' failed' : ''

  if (dot) return <span className={`download-dot${tone}`} aria-hidden="true" />

  const label =
    active.state === 'Ready'
      ? 'hotovo'
      : active.state === 'Failed'
        ? 'chyba'
        : active.progress !== null
          ? `${Math.round(active.progress * 100)} %`
          : '…'

  return <span className={`download-badge${tone}`}>{label}</span>
}
