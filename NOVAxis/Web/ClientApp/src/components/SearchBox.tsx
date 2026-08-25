import { useEffect, useRef, useState } from 'react'

import { api, TrackDto } from '../api'
import { formatDuration } from '../format'
import { Note, Plus, Search } from '../Icons'
import { useToast } from '../Toast'

/**
 * Search-as-you-type over the bot's own lookup. Picking a result queues it;
 * Enter queues whatever the query resolves to - a pasted link included.
 */
export function SearchBox({ guildId }: { guildId: string }) {
  const { run, toast } = useToast()
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<TrackDto[] | null>(null)
  const [searching, setSearching] = useState(false)
  const [open, setOpen] = useState(false)

  const debounce = useRef<number>()
  const box = useRef<HTMLDivElement>(null)

  useEffect(() => {
    window.clearTimeout(debounce.current)

    const trimmed = query.trim()

    if (trimmed.length < 2) {
      setResults(null)
      setSearching(false)
      return
    }

    setSearching(true)

    debounce.current = window.setTimeout(() => {
      api
        .search(guildId, trimmed)
        .then(found => {
          setResults(found)
          setOpen(true)
        })
        .catch(() => setResults([]))
        .finally(() => setSearching(false))
    }, 350)
  }, [query, guildId])

  // Clicking anywhere else puts the panel away
  useEffect(() => {
    const close = (event: MouseEvent) => {
      if (!box.current?.contains(event.target as Node)) setOpen(false)
    }

    window.addEventListener('mousedown', close)
    return () => window.removeEventListener('mousedown', close)
  }, [])

  const enqueue = (input: string, title?: string) => {
    setOpen(false)
    setQuery('')
    setResults(null)

    run(
      api.play(guildId, input).then(response => {
        if (response.enqueued > 1)
          toast(`Přidáno do fronty: ${response.playlistName ?? 'playlist'} (${response.enqueued})`)
        else toast(`Přidáno do fronty: ${title ?? response.track?.title ?? input}`)
      }),
    )
  }

  return (
    <div className="search" ref={box}>
      <form
        className="search-field"
        onSubmit={e => {
          e.preventDefault()
          if (query.trim()) enqueue(query.trim())
        }}
      >
        <Search size={16} />
        <input
          value={query}
          placeholder="Hledej skladbu, nebo vlož odkaz…"
          aria-label="Vyhledat skladbu"
          onChange={e => setQuery(e.target.value)}
          onFocus={() => results && setOpen(true)}
          onKeyDown={e => e.key === 'Escape' && setOpen(false)}
        />
        {searching && <span className="search-spinner" aria-label="Hledám" />}
      </form>

      {open && results && (
        <div className="search-panel">
          {results.length === 0 && <p className="empty-note">Nepodařilo se nic najít.</p>}
          {results.map((track, index) => (
            <button
              type="button"
              key={`${track.uri ?? track.title}-${index}`}
              className="search-row"
              onClick={() => enqueue(track.uri ?? track.title, track.title)}
            >
              {track.artworkUri ? (
                <img src={track.artworkUri} alt="" />
              ) : (
                <span className="search-art-fallback">
                  <Note size={16} />
                </span>
              )}
              <span className="search-titles">
                <span className="search-title">{track.title}</span>
                {track.author && <span className="search-author">{track.author}</span>}
              </span>
              <span className="search-duration">
                {formatDuration(track.durationMs, track.isLiveStream)}
              </span>
              <span className="search-add" aria-hidden="true">
                <Plus size={16} />
              </span>
            </button>
          ))}
        </div>
      )}
    </div>
  )
}
