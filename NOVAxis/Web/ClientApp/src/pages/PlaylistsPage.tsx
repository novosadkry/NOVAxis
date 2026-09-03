import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'

import { api, ApiError, PlaylistDto } from '../api'
import { formatTotal } from '../format'
import { useGuilds } from '../guilds'
import { usePlayerState } from '../player'
import { useToast } from '../Toast'
import { AppShell } from '../components/AppShell'
import { PlayerBar } from '../components/PlayerBar'
import { Close, Note, Play, Plus } from '../Icons'

function describeFailure(error: unknown): string {
  if (error instanceof ApiError) return error.message
  return 'Nastala neznámá chyba'
}

function tracks(count: number): string {
  return count === 1 ? 'skladba' : count < 5 ? 'skladby' : 'skladeb'
}

/**
 * Saved queues, for whichever guild was last open. A playlist is not a guild's - it is a
 * person's - but loading one has to happen somewhere, so the page borrows the guild the
 * player was last pointed at, the same way the downloads page does.
 */
export function PlaylistsPage() {
  const { lastGuildId } = useGuilds()
  const { run, toast } = useToast()

  const guildId = lastGuildId ?? ''
  const live = usePlayerState(guildId || null)

  const [playlists, setPlaylists] = useState<PlaylistDto[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [open, setOpen] = useState<PlaylistDto | null>(null)
  const [name, setName] = useState('')
  const [busy, setBusy] = useState(false)

  const reload = useCallback(() => {
    api
      .playlists(guildId || undefined)
      .then(found => {
        setPlaylists(found)
        setError(null)
      })
      .catch(problem => {
        setPlaylists([])
        setError(describeFailure(problem))
      })
  }, [guildId])

  useEffect(reload, [reload])

  const queued = (live.state?.current ? 1 : 0) + (live.state?.queue.length ?? 0)

  const save = async () => {
    if (!name.trim() || !guildId || busy) return

    setBusy(true)

    try {
      const saved = await api.savePlaylist(guildId, name.trim(), false)
      setName('')
      reload()
      toast(`Uloženo jako „${saved.name}“`)
    } catch (problem) {
      toast(describeFailure(problem))
    } finally {
      setBusy(false)
    }
  }

  const load = (playlist: PlaylistDto, replace: boolean) => {
    if (!guildId) return

    run(
      api.loadPlaylist(playlist.id, guildId, replace).then(() => {
        toast(`Zařazeno: „${playlist.name}“`)
      }),
    )
  }

  const share = (playlist: PlaylistDto) => {
    if (!guildId) return

    run(
      api.sharePlaylist(playlist.id, guildId, !playlist.shared).then(() => {
        reload()
        toast(playlist.shared ? 'Playlist je zase jen tvůj' : 'Playlist je k dispozici serveru')
      }),
    )
  }

  const remove = (playlist: PlaylistDto) => {
    setPlaylists(current => current?.filter(x => x.id !== playlist.id) ?? null)

    run(
      api.deletePlaylist(playlist.id).then(() => {
        if (open?.id === playlist.id) setOpen(null)
        toast(`Playlist „${playlist.name}“ smazán`)
      }),
    )
  }

  const show = (playlist: PlaylistDto) => {
    if (open?.id === playlist.id) {
      setOpen(null)
      return
    }

    // The listing carries no tracks - a page of twenty playlists has no use for them
    api
      .playlist(playlist.id, guildId || undefined)
      .then(setOpen)
      .catch(problem => toast(describeFailure(problem)))
  }

  return (
    <AppShell
      activeTool="playlists"
      title="Playlisty"
      subtitle="uložené fronty · tvoje a serverové"
      backTo={guildId ? `/g/${guildId}` : '/'}
      bar={guildId ? <PlayerBar guildId={guildId} live={live} /> : undefined}
    >
      {!guildId && (
        <p className="empty-note">
          Otevři nejdřív nějaký server — playlist se odněkud ukládá a někam zařazuje.{' '}
          <Link to="/">Vybrat server</Link>
        </p>
      )}

      {guildId && (
        <section className="panel">
          <header className="queue-head">
            <h3 className="eyebrow">ULOŽIT AKTUÁLNÍ FRONTU</h3>
            <span className="queue-total">
              {queued} {tracks(queued)}
            </span>
          </header>

          <form
            className="playlist-save"
            onSubmit={e => {
              e.preventDefault()
              void save()
            }}
          >
            <input
              value={name}
              maxLength={60}
              placeholder="Jméno playlistu…"
              aria-label="Jméno playlistu"
              onChange={e => setName(e.target.value)}
            />
            <button type="submit" className="btn-ghost" disabled={!name.trim() || queued === 0 || busy}>
              {busy ? <span className="btn-spinner" aria-hidden="true" /> : <Plus size={16} />}
              Uložit
            </button>
          </form>

          {queued === 0 && (
            <p className="empty-note">Fronta je prázdná — není co uložit.</p>
          )}
        </section>
      )}

      {error && <p className="empty-note">{error}</p>}

      {playlists !== null && playlists.length === 0 && !error && (
        <p className="empty-note">Zatím tu žádný playlist nemáš.</p>
      )}

      {playlists && playlists.length > 0 && (
        <section className="panel">
          <header className="queue-head">
            <h3 className="eyebrow">ULOŽENÉ · {playlists.length}</h3>
          </header>

          <ul className="download-list">
            {playlists.map(playlist => (
              <li className="download-item playlist-item" key={playlist.id}>
                <div className="playlist-row">
                  <button
                    type="button"
                    className="playlist-open"
                    aria-expanded={open?.id === playlist.id}
                    onClick={() => show(playlist)}
                  >
                    <span className="playlist-art" aria-hidden="true">
                      <Note size={16} />
                    </span>
                    <span className="queue-titles">
                      <span className="queue-title">{playlist.name}</span>
                      <span className="queue-author">
                        {playlist.trackCount} {tracks(playlist.trackCount)}
                        {playlist.totalMs > 0 && ` · ${formatTotal(playlist.totalMs)}`}
                        {!playlist.mine && ` · od ${playlist.ownerName ?? 'někoho'}`}
                        {playlist.mine && playlist.shared && ' · sdílený'}
                      </span>
                    </span>
                  </button>

                  <div className="playlist-actions">
                    <button
                      type="button"
                      className="btn-ghost"
                      disabled={!guildId || playlist.trackCount === 0}
                      onClick={() => load(playlist, false)}
                    >
                      <Play size={14} />
                      Zařadit
                    </button>

                    {playlist.mine && (
                      <>
                        <button
                          type="button"
                          className="text-btn"
                          onClick={() => share(playlist)}
                        >
                          {playlist.shared ? 'Nesdílet' : 'Sdílet'}
                        </button>

                        <button
                          type="button"
                          className="icon-btn"
                          aria-label={`Smazat ${playlist.name}`}
                          onClick={() => remove(playlist)}
                        >
                          <Close size={16} />
                        </button>
                      </>
                    )}
                  </div>
                </div>

                {open?.id === playlist.id && (
                  <ol className="playlist-tracks">
                    {open.tracks.map((track, index) => (
                      <li key={`${track.uri ?? track.title}-${index}`}>
                        <span className="queue-index">{String(index + 1).padStart(2, '0')}</span>
                        <span className="queue-titles">
                          <span className="queue-title">{track.title}</span>
                          {track.author && <span className="queue-author">{track.author}</span>}
                        </span>
                      </li>
                    ))}
                  </ol>
                )}
              </li>
            ))}
          </ul>
        </section>
      )}
    </AppShell>
  )
}
