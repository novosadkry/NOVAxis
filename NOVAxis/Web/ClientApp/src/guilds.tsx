import { createContext, useCallback, useContext, useEffect, useState } from 'react'

import { api, GuildDto } from './api'

interface GuildsLive {
  guilds: GuildDto[]
  /** True until the first answer, so a picker can tell empty from not yet known. */
  loading: boolean
  /** The guild last opened, so the tools keep its transport and its highlight. */
  lastGuildId: string | null
  remember: (guildId: string) => void
}

const GuildsContext = createContext<GuildsLive>({
  guilds: [],
  loading: true,
  lastGuildId: null,
  remember: () => undefined,
})

/**
 * The guild list, fetched once for the whole session. It used to be fetched by the shell,
 * which every page mounts anew - so the sidebar emptied and refilled on every navigation.
 *
 * It also remembers which guild was last opened. Downloads are not a guild's, but the
 * page showing them still sits in that guild's frame, and a route cannot say so now that
 * there is only one of it. Deliberately not persisted: it says where you just were, and
 * on a cold start - a link handed out on Discord, say - you were nowhere.
 */
export function GuildsProvider({ children }: { children: React.ReactNode }) {
  const [guilds, setGuilds] = useState<GuildDto[]>([])
  const [loading, setLoading] = useState(true)
  const [lastGuildId, setLastGuildId] = useState<string | null>(null)

  useEffect(() => {
    api
      .guilds()
      .catch(() => [])
      .then(found => {
        setGuilds(found)
        setLoading(false)
      })
  }, [])

  const remember = useCallback((guildId: string) => {
    setLastGuildId(current => (current === guildId ? current : guildId))
  }, [])

  return (
    <GuildsContext.Provider value={{ guilds, loading, lastGuildId, remember }}>
      {children}
    </GuildsContext.Provider>
  )
}

export const useGuilds = () => useContext(GuildsContext)
