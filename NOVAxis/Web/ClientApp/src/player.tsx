import { createContext, useCallback, useContext, useEffect, useRef, useState } from 'react'
import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr'

import { api, PlayerStateDto } from './api'
import { LiveState } from './live'

const Idle: LiveState = { state: null, receivedAt: 0, error: null }

interface PlayerLive extends LiveState {
  /** Points the one connection at a guild, or at none. */
  watch: (guildId: string | null) => void
}

const PlayerContext = createContext<PlayerLive>({ ...Idle, watch: () => undefined })

/**
 * One socket for the whole session, following whichever guild is being looked at, with
 * polling as the fallback where a socket cannot be had. Every snapshot is stamped with
 * the client clock so the progress bar interpolates without trusting the server's.
 *
 * The connection used to belong to the hook, which every page mounts anew - so moving
 * between the player and the downloads tore it down and built it again, and the transport
 * blanked until a fresh snapshot arrived. Here it outlives the navigation, and staying on
 * the same guild is not a change of subscription at all.
 */
export function PlayerProvider({ children }: { children: React.ReactNode }) {
  const [guildId, setGuildId] = useState<string | null>(null)
  const [live, setLive] = useState<LiveState>(Idle)
  const [transport, setTransport] = useState<'connecting' | 'hub' | 'polling'>('connecting')

  const hub = useRef<HubConnection | null>(null)

  // Which guild counts is read from a ref: the snapshot handler is registered once and
  // outlives every subscription made over it
  const watched = useRef<string | null>(null)

  const watch = useCallback((next: string | null) => {
    setGuildId(current => (current === next ? current : next))
  }, [])

  const receive = useCallback((state: PlayerStateDto) => {
    if (state?.guildId !== watched.current) return
    setLive({ state, receivedAt: Date.now(), error: null })
  }, [])

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl('/hub/player')
      .withAutomaticReconnect()
      .build()

    connection.on('state', receive)

    connection.onreconnected(() => {
      const following = watched.current

      if (following)
        void connection.invoke<PlayerStateDto>('Subscribe', following).then(receive).catch(() => undefined)
    })

    hub.current = connection

    connection
      .start()
      .then(() => setTransport('hub'))
      .catch(() => setTransport('polling'))

    return () => {
      hub.current = null
      connection.stop().catch(() => undefined)
    }
  }, [receive])

  useEffect(() => {
    watched.current = guildId

    // Another guild is another player, and its state must not be shown under this one
    setLive(prev => (prev.state !== null && prev.state.guildId !== guildId ? Idle : prev))

    if (guildId === null || transport === 'connecting')
      return

    let disposed = false
    let poller: number | undefined

    if (transport === 'polling') {
      const tick = () => api.state(guildId).then(receive).catch(() => undefined)

      tick()
      poller = window.setInterval(tick, 3000)
    } else {
      hub.current
        ?.invoke<PlayerStateDto>('Subscribe', guildId)
        .then(receive)
        .catch(() => {
          if (!disposed) setLive(prev => ({ ...prev, error: 'Nejste členem tohoto serveru' }))
        })
    }

    return () => {
      disposed = true

      if (poller !== undefined) window.clearInterval(poller)

      // Leaving the group stops the server ticking for a guild nobody is watching
      if (transport === 'hub')
        hub.current?.invoke('Unsubscribe', guildId).catch(() => undefined)
    }
  }, [guildId, transport, receive])

  return (
    <PlayerContext.Provider value={{ ...live, watch }}>{children}</PlayerContext.Provider>
  )
}

/**
 * Follows a guild for as long as the caller is on screen. Asking for the guild already
 * being followed changes nothing, which is what keeps the transport steady when a page
 * hands over to another within the same guild.
 */
export function usePlayerState(guildId: string | null): LiveState {
  const { watch, ...live } = useContext(PlayerContext)

  useEffect(() => {
    watch(guildId || null)
  }, [guildId, watch])

  return live
}
