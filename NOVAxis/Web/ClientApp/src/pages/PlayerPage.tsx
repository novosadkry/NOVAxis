import { useCallback, useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'

import { api, TrackDto } from '../api'
import { useGuilds } from '../guilds'
import { usePlayerState } from '../player'
import { describeFailure, isLive, useDownloads } from '../downloads'
import { useToast } from '../Toast'
import { AppShell } from '../components/AppShell'
import { NowPlaying } from '../components/NowPlaying'
import { PlayerBar } from '../components/PlayerBar'
import { QueueList } from '../components/QueueList'
import { SearchBox } from '../components/SearchBox'

/**
 * What the core is playing: the now-playing hero, the queue and the transport bar.
 * Everything here redraws from one live state.
 */
export function PlayerPage() {
  const { guildId = '' } = useParams()
  const live = usePlayerState(guildId)

  // So the downloads page, which has no guild of its own, keeps this one's frame
  const { remember } = useGuilds()

  useEffect(() => {
    if (guildId) remember(guildId)
  }, [guildId, remember])

  const { overview, reload } = useDownloads()
  const { toast } = useToast()
  const [starting, setStarting] = useState<string | null>(null)

  const active = overview?.active ?? null

  const quickDownload = useCallback(
    async (track: TrackDto) => {
      if (!track.uri || starting) return

      // Only one link lives at a time, so say so rather than quietly taking the last one
      const replacing = isLive(active)

      setStarting(track.uri)

      try {
        await api.startDownload(track.uri, 'Audio', '', track.title)
        reload()
        toast(
          replacing
            ? `Stahuji „${track.title}“ — předchozí odkaz byl nahrazen`
            : `Stahuji „${track.title}“`,
        )
      } catch (error) {
        toast(describeFailure(error))
      } finally {
        setStarting(null)
      }
    },
    [active, reload, starting, toast],
  )

  const state = live.state

  return (
    <AppShell
      activeGuildId={guildId}
      subtitle={
        state?.connected && state.voiceChannel
          ? `připojeno · ${state.voiceChannel.name}`
          : 'jádro v klidovém režimu'
      }
      actions={<SearchBox guildId={guildId} />}
      bar={<PlayerBar guildId={guildId} live={live} />}
    >
      {live.error && <p className="empty-note">{live.error}</p>}

      {!live.error && (
        <>
          <NowPlaying live={live} onDownload={quickDownload} startingUri={starting} />
          <QueueList
            guildId={guildId}
            state={state}
            onDownload={quickDownload}
            startingUri={starting}
          />
        </>
      )}
    </AppShell>
  )
}
