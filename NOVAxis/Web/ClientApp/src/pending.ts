import { useCallback, useEffect, useRef, useState } from 'react'

import { PlayResponse, TrackDto } from './api'

/** How long a request may go unanswered before its row is given up on. */
const AbandonAfterMs = 30000

export interface PendingTrack {
  id: number
  /** What the search already knew - a typed phrase or a pasted link knows nothing yet. */
  known: TrackDto | null
  /** What to call it until the server has an answer. */
  label: string
  /**
   * Nothing was playing when it was asked for, so this is the track about to play
   * rather than one joining the queue. Decided once, at the click: derived per render
   * it would hop out of the hero and into the queue the moment playback started,
   * reading as a duplicate of the track that had just begun.
   */
  hero: boolean
}

interface Waiting extends PendingTrack {
  /**
   * Client clock at which the server confirmed the addition. A snapshot older than this
   * was taken before the track was queued, so it is no proof the row can go.
   */
  ackAt: number
}

/**
 * Rows for tracks the server has been asked for but has not shown yet.
 *
 * Adding one means an extractor run, which is seconds - and for all of them the queue
 * sat unchanged, so the only sign a click had done anything was the toast. A row stands
 * in from the moment of the click, carrying whatever the search already knew, and gives
 * way once a snapshot taken after the server confirmed the addition arrives to replace it.
 */
export function usePendingTracks(receivedAt: number) {
  const [waiting, setWaiting] = useState<Waiting[]>([])

  const nextId = useRef(0)
  const timers = useRef<number[]>([])

  useEffect(() => {
    const stop = timers.current
    return () => stop.forEach(window.clearTimeout)
  }, [])

  useEffect(() => {
    setWaiting(current => {
      const held = current.filter(entry => receivedAt <= entry.ackAt)
      return held.length === current.length ? current : held
    })
  }, [receivedAt])

  const expect = useCallback(
    (label: string, known: TrackDto | null, request: Promise<PlayResponse>, hero: boolean) => {
      const id = ++nextId.current

      const forget = () => setWaiting(current => current.filter(entry => entry.id !== id))

      const acknowledge = (patch: Partial<Waiting>) =>
        setWaiting(current =>
          current.map(entry => (entry.id === id ? { ...entry, ...patch, ackAt: Date.now() } : entry)),
        )

      setWaiting(current => [...current, { id, known, label, hero, ackAt: Infinity }])

      // A request nobody ever answers must not leave a row waiting for good
      const abandon = window.setTimeout(forget, AbandonAfterMs)
      timers.current.push(abandon)

      request.then(
        response => {
          window.clearTimeout(abandon)

          // One request, many tracks - the row stands for the playlist until they land
          acknowledge(
            response.enqueued > 1
              ? { label: `${response.playlistName ?? 'playlist'} (${response.enqueued})` }
              : { known: response.track ?? known },
          )
        },
        () => {
          window.clearTimeout(abandon)
          forget()
        },
      )
    },
    [],
  )

  return { pending: waiting as PendingTrack[], expect }
}
