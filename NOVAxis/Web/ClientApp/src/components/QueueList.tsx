import { useEffect, useState } from 'react'

import { api, PlayerStateDto, QueueItemDto } from '../api'
import { formatDuration, formatTotal } from '../format'
import { Close, Grip } from '../Icons'
import { useToast } from '../Toast'

interface QueueListProps {
  guildId: string
  state: PlayerStateDto | null
}

/**
 * The waiting tracks, in play order. Rows drag to reorder and every mutation is
 * applied locally first - the next server snapshot settles the truth.
 */
export function QueueList({ guildId, state }: QueueListProps) {
  const { run } = useToast()
  const [items, setItems] = useState<QueueItemDto[]>([])
  const [dragId, setDragId] = useState<string | null>(null)
  const [overIndex, setOverIndex] = useState<number | null>(null)

  useEffect(() => {
    setItems(state?.queue ?? [])
  }, [state])

  if (!state?.connected) return null

  const totalMs = items.reduce((total, item) => total + item.track.durationMs, 0)

  const drop = (toIndex: number) => {
    if (dragId === null) return

    const fromIndex = items.findIndex(i => i.requestId === dragId)

    setDragId(null)
    setOverIndex(null)

    if (fromIndex < 0) return

    // Dropping below the original spot shifts the target up by the removed row
    const target = toIndex > fromIndex ? toIndex - 1 : toIndex

    if (target === fromIndex) return

    const next = [...items]
    const [moved] = next.splice(fromIndex, 1)
    next.splice(target, 0, moved)

    setItems(next)
    run(api.moveItem(guildId, moved.requestId, target))
  }

  const remove = (item: QueueItemDto) => {
    setItems(current => current.filter(i => i.requestId !== item.requestId))
    run(api.removeItem(guildId, item.requestId))
  }

  return (
    <section className="queue">
      <header className="queue-head">
        <h3 className="eyebrow">
          FRONTA · {items.length}
          {items.length > 0 && <span className="queue-total"> / {formatTotal(totalMs)}</span>}
        </h3>
        {items.length > 0 && (
          <button type="button" className="text-btn" onClick={() => run(api.clearQueue(guildId))}>
            Vyprázdnit
          </button>
        )}
      </header>

      {items.length === 0 && (
        <p className="empty-note">Fronta je prázdná — vyhledej, co má hrát dál.</p>
      )}

      <ol className="queue-list" onDragLeave={() => setOverIndex(null)}>
        {items.map((item, index) => (
          <li
            key={item.requestId}
            className={
              'queue-row' +
              (dragId === item.requestId ? ' dragging' : '') +
              (overIndex === index ? ' drop-before' : '')
            }
            draggable
            onDragStart={e => {
              setDragId(item.requestId)
              e.dataTransfer.effectAllowed = 'move'
            }}
            onDragEnd={() => {
              setDragId(null)
              setOverIndex(null)
            }}
            onDragOver={e => {
              e.preventDefault()
              setOverIndex(index)
            }}
            onDrop={e => {
              e.preventDefault()
              drop(index)
            }}
          >
            <span className="queue-grip" aria-hidden="true">
              <Grip size={14} />
            </span>
            <span className="queue-index">{String(index + 1).padStart(2, '0')}</span>
            <span className="queue-titles">
              <span className="queue-title">{item.track.title}</span>
              {item.track.author && <span className="queue-author">{item.track.author}</span>}
            </span>
            {item.requestedBy && (
              <span className="queue-requester" title={`vyžádal ${item.requestedBy.name}`}>
                {item.requestedBy.avatarUrl ? (
                  <img src={item.requestedBy.avatarUrl} alt={item.requestedBy.name} />
                ) : (
                  item.requestedBy.name
                )}
              </span>
            )}
            <span className="queue-duration">
              {formatDuration(item.track.durationMs, item.track.isLiveStream)}
            </span>
            <button
              type="button"
              className="icon-btn queue-remove"
              aria-label={`Odebrat ${item.track.title}`}
              onClick={() => remove(item)}
            >
              <Close size={16} />
            </button>
          </li>
        ))}

        {/* A drop zone below the last row, so a track can land at the very end */}
        {dragId !== null && (
          <li
            className={'queue-tail' + (overIndex === items.length ? ' drop-before' : '')}
            onDragOver={e => {
              e.preventDefault()
              setOverIndex(items.length)
            }}
            onDrop={e => {
              e.preventDefault()
              drop(items.length)
            }}
          />
        )}
      </ol>
    </section>
  )
}
