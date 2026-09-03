import { createContext, useCallback, useContext, useEffect, useRef, useState } from 'react'
import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr'

import { api, PlayerStateDto } from './api'
import { LiveState } from './live'

const Idle: LiveState = { state: null, receivedAt: 0, error: null }

/**
 * 'connecting' is the one-time initial handshake; 'reconnecting' is the same "no data
 * right now" situation returned to later, kept distinct so the UI can say "still there,
 * hold on" rather than repeating the first-load message. 'polling' means automatic
 * reconnect gave up and the fallback has taken over for good, until the guild changes.
 */
type Transport = 'connecting' | 'hub' | 'reconnecting' | 'polling'

interface PlayerLive extends LiveState {
  /** Points the one connection at a guild, or at none. */
  watch: (guildId: string | null) => void
  transport: Transport
  /** The guild actually being followed right now, for callers with no live state of their own. */
  guildId: string | null
}

const PlayerContext = createContext<PlayerLive>({
  ...Idle,
  watch: () => undefined,
  transport: 'connecting',
  guildId: null,
})

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
  const [transport, setTransport] = useState<Transport>('connecting')

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

    // connection.stop() on unmount fires onclose itself - without this, that teardown
    // would flip a dead component's transport to 'polling' behind its own back
    let stopped = false

    connection.on('state', receive)

    // Not a verdict yet - SignalR is retrying on its own schedule. Only onreconnected
    // or onclose settle what actually happened
    connection.onreconnecting(() => {
      if (!stopped) setTransport('reconnecting')
    })

    connection.onreconnected(() => {
      if (stopped) return

      setTransport('hub')

      const following = watched.current

      if (following)
        void connection.invoke<PlayerStateDto>('Subscribe', following).then(receive).catch(() => undefined)
    })

    // Automatic reconnect exhausted its attempts - polling is what keeps the position
    // honest instead of quietly extrapolating over a socket nobody is coming back on
    connection.onclose(() => {
      if (!stopped) setTransport('polling')
    })

    hub.current = connection

    connection
      .start()
      .then(() => {
        if (!stopped) setTransport('hub')
      })
      .catch(() => {
        if (!stopped) setTransport('polling')
      })

    return () => {
      stopped = true
      hub.current = null
      connection.stop().catch(() => undefined)
    }
  }, [receive])

  useEffect(() => {
    watched.current = guildId

    // Another guild is another player, and its state must not be shown under this one
    setLive(prev => (prev.state !== null && prev.state.guildId !== guildId ? Idle : prev))

    // While 'reconnecting', SignalR itself is mid-retry on the very socket a Subscribe
    // would need - invoking through it now would just surface a network hiccup as if it
    // were the server refusing the guild. Wait for a verdict: back to 'hub', or 'polling'
    if (guildId === null || transport === 'connecting' || transport === 'reconnecting')
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
    <PlayerContext.Provider value={{ ...live, watch, transport, guildId }}>
      {children}
    </PlayerContext.Provider>
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

/**
 * Read-only look at the one connection's health, for chrome that sits outside whichever
 * page is following a guild (the shell around it) and so has no live state of its own to
 * derive this from. Never calls watch() - looking at the socket must not steer it.
 */
export function usePlayerTransport(): { transport: Transport; guildId: string | null } {
  const { transport, guildId } = useContext(PlayerContext)
  return { transport, guildId }
}
